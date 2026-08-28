using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using MediaLock.Core.Media;

namespace MediaLock.Application;

public sealed record MediaTargetAdapterRegistration
{
    public MediaTargetAdapterRegistration(
        MediaTargetProviderId provider,
        IMediaTargetCatalog catalog,
        IMediaTargetController controller)
    {
        if (string.IsNullOrWhiteSpace(provider.Value))
        {
            throw new ArgumentException("A Media Target provider is required.", nameof(provider));
        }

        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(controller);
        Provider = provider;
        Catalog = catalog;
        Controller = controller;
    }

    public MediaTargetProviderId Provider { get; }

    public IMediaTargetCatalog Catalog { get; }

    public IMediaTargetController Controller { get; }
}

public sealed class CompositeMediaTargetAdapter :
    IMediaTargetCatalog,
    IMediaTargetController,
    IMediaTargetAuthorizationController
{
    private readonly ImmutableArray<MediaTargetAdapterRegistration> registrations;
    private readonly IReadOnlyDictionary<MediaTargetProviderId, IMediaTargetController> controllers;
    private readonly IReadOnlyDictionary<MediaTargetProviderId, IMediaTargetAuthorizationController>
        authorizationControllers;
    private int disposed;

    public CompositeMediaTargetAdapter(
        MediaTargetAdapterRegistration primary,
        params MediaTargetAdapterRegistration[] optional)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(optional);
        registrations = [primary, .. optional];
        if (registrations.Select(registration => registration.Provider).Distinct().Count() !=
            registrations.Length)
        {
            throw new ArgumentException(
                "Every Media Target Adapter registration must use a distinct provider.",
                nameof(optional));
        }

        controllers = registrations.ToDictionary(
            registration => registration.Provider,
            registration => registration.Controller);
        authorizationControllers = registrations
            .Where(registration => registration.Controller is IMediaTargetAuthorizationController)
            .ToDictionary(
                registration => registration.Provider,
                registration => (IMediaTargetAuthorizationController)registration.Controller);
    }

    public async IAsyncEnumerable<MediaTargetCatalogSnapshot> WatchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var updates = Channel.CreateBounded<ProviderUpdate>(new BoundedChannelOptions(64)
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait,
        });
        var workers = registrations
            .Select((registration, index) => WatchProviderAsync(
                registration,
                index == 0,
                updates.Writer,
                lifetime.Token))
            .ToArray();
        var completion = CompleteUpdatesAsync(workers, updates.Writer);
        var snapshots = new Dictionary<MediaTargetProviderId, MediaTargetCatalogSnapshot>();

        try
        {
            await foreach (var update in updates.Reader.ReadAllAsync(cancellationToken))
            {
                if (update.Error is not null)
                {
                    if (update.IsPrimary)
                    {
                        throw new InvalidOperationException(
                            "The primary Media Target provider stopped unexpectedly.",
                            update.Error);
                    }

                    snapshots[update.Provider] = EmptyOptionalSnapshot;
                }
                else if (update.Snapshot is { } snapshot)
                {
                    ValidateProviderSnapshot(update.Provider, snapshot);
                    snapshots[update.Provider] = snapshot;
                }
                else if (!update.IsPrimary)
                {
                    snapshots[update.Provider] = EmptyOptionalSnapshot;
                }

                if (!snapshots.TryGetValue(registrations[0].Provider, out var primary))
                {
                    continue;
                }

                yield return Merge(primary, snapshots);
            }
        }
        finally
        {
            await lifetime.CancelAsync();
            try
            {
                await completion;
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
            {
                Trace.TraceInformation("Media Target provider composition stopped.");
            }
        }
    }

    public ValueTask<MediaCommandOutcome> TryExecuteAsync(
        MediaTargetId target,
        MediaCommand command,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        return controllers.TryGetValue(target.Provider, out var controller)
            ? controller.TryExecuteAsync(target, command, cancellationToken)
            : ValueTask.FromResult(MediaCommandOutcome.Rejected);
    }

    public ValueTask<bool> RevokeAsync(
        MediaTargetId target,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        return authorizationControllers.TryGetValue(target.Provider, out var controller)
            ? controller.RevokeAsync(target, cancellationToken)
            : ValueTask.FromResult(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        var disposedCatalogs = new HashSet<IMediaTargetCatalog>(ReferenceEqualityComparer.Instance);
        foreach (var registration in registrations)
        {
            if (disposedCatalogs.Add(registration.Catalog))
            {
                await registration.Catalog.DisposeAsync();
            }
        }
    }

    private static async Task WatchProviderAsync(
        MediaTargetAdapterRegistration registration,
        bool isPrimary,
        ChannelWriter<ProviderUpdate> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var snapshot in registration.Catalog.WatchAsync(cancellationToken))
            {
                await writer.WriteAsync(
                    new ProviderUpdate(registration.Provider, isPrimary, snapshot, null),
                    cancellationToken);
            }

            await writer.WriteAsync(
                new ProviderUpdate(registration.Provider, isPrimary, null, null),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Trace.TraceInformation(
                "Media Target provider '{0}' stopped.",
                registration.Provider);
        }
        catch (Exception exception)
        {
            writer.TryWrite(new ProviderUpdate(
                registration.Provider,
                isPrimary,
                null,
                exception));
        }
    }

    private static async Task CompleteUpdatesAsync(
        Task[] workers,
        ChannelWriter<ProviderUpdate> writer)
    {
        try
        {
            await Task.WhenAll(workers);
            writer.TryComplete();
        }
        catch (Exception exception)
        {
            writer.TryComplete(exception);
        }
    }

    private static MediaTargetCatalogSnapshot Merge(
        MediaTargetCatalogSnapshot primary,
        IReadOnlyDictionary<MediaTargetProviderId, MediaTargetCatalogSnapshot> snapshots) =>
        new(
            snapshots.Values.SelectMany(snapshot => snapshot.ObservedTargets).ToImmutableArray(),
            primary.WindowsCurrentTarget,
            snapshots.Values.SelectMany(snapshot => snapshot.AuthoritativeCorrelations).ToImmutableArray(),
            primary.Status,
            primary.StatusMessage);

    private static void ValidateProviderSnapshot(
        MediaTargetProviderId provider,
        MediaTargetCatalogSnapshot snapshot)
    {
        if (snapshot.ObservedTargets.Any(target => target.Id.Provider != provider))
        {
            throw new InvalidDataException(
                $"Media Target provider '{provider}' published a target owned by another provider.");
        }
    }

    private static MediaTargetCatalogSnapshot EmptyOptionalSnapshot { get; } = new(
        [],
        null,
        [],
        MediaSessionCatalogStatus.Unavailable);

    private sealed record ProviderUpdate(
        MediaTargetProviderId Provider,
        bool IsPrimary,
        MediaTargetCatalogSnapshot? Snapshot,
        Exception? Error);
}
