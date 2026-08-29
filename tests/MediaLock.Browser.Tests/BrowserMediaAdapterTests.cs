using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediaLock.Core.Media;
using Xunit;

namespace MediaLock.Browser.Tests;

public sealed class BrowserMediaAdapterTests
{
    private const string ExtensionId = "kggfkkiifnclhhmibdglkbdfbacakemn";

    [Fact]
    public async Task AuthorizedSnapshotAndCommandCrossTheFramedConnectionOnce()
    {
        await using var adapter = new BrowserMediaAdapter(new BrowserMediaAdapterOptions(ExtensionId));
        await using var connection = await TestConnection.ConnectAsync(adapter);
        var hello = await connection.ReadAsync();
        var hostNonce = hello.GetProperty("hostNonce").GetString()!;
        const string extensionNonce = "11111111-1111-4111-8111-111111111111";
        const string profileId = "22222222-2222-4222-8222-222222222222";
        var connectionId = DeriveConnectionId(hostNonce, extensionNonce, profileId);
        await connection.WriteAsync(new
        {
            protocolVersion = 2,
            type = "extensionHello",
            hostNonce,
            extensionNonce,
            extensionId = ExtensionId,
            browserFamily = "brave",
            profileId,
            capabilities = new[] { "pause", "play", "seek", "toggle" },
        });
        var ack = await connection.ReadAsync();
        Assert.Equal(connectionId, ack.GetProperty("connectionId").GetString());

        await connection.WriteAsync(new
        {
            protocolVersion = 2,
            type = "targetSnapshot",
            connectionId,
            sequence = 1,
            target = new
            {
                bindingId = "page-binding",
                endpointId = "media-0",
                scope = "temporary",
                tabId = 7,
                frameId = 0,
                documentId = "document-1",
                pageOrigin = "https://example.com",
            },
            presentation = new
            {
                sourceDisplayName = "Big Buck Bunny — Brave",
                playbackStatus = "playing",
                playbackRate = 1.75,
                capabilities = new[] { "pause", "play", "seek", "toggle" },
                observedAt = "2026-08-27T00:00:00Z",
                timeline = new
                {
                    startSeconds = 0,
                    endSeconds = 600,
                    positionSeconds = 30,
                },
            },
        });

        var snapshot = await FirstAsync(
            adapter,
            value => value.ObservedTargets.Length == 1);
        var target = Assert.Single(snapshot.ObservedTargets);
        Assert.Equal(MediaTargetProviderId.Browser, target.Id.Provider);
        Assert.Equal("Big Buck Bunny — Brave", target.Presentation.SourceDisplayName);
        Assert.Equal("browser-family:brave", target.Presentation.SourceGroup?.Key);
        Assert.Equal("Brave", target.Presentation.SourceGroup?.DisplayName);
        Assert.Equal(1.75, target.Presentation.ReportedPlaybackRate);
        Assert.Equal(1.75, target.Presentation.PlaybackRate.Rate);
        Assert.Equal(
            PlaybackRateResolutionSource.Reported,
            target.Presentation.PlaybackRate.Source);

        var dispatch = adapter.TryExecuteAsync(
            target.Id,
            MediaCommand.TogglePlayPause,
            CancellationToken.None).AsTask();
        var command = await connection.ReadAsync();
        Assert.Equal("command", command.GetProperty("type").GetString());
        Assert.Equal("toggle", command.GetProperty("command").GetProperty("name").GetString());
        var requestId = command.GetProperty("requestId").GetString();
        await connection.WriteAsync(new
        {
            protocolVersion = 2,
            type = "commandResult",
            connectionId,
            sequence = 2,
            requestId,
            accepted = true,
            errorCode = (string?)null,
        });

        Assert.Equal(MediaCommandOutcome.Succeeded, await dispatch);

        await connection.WriteAsync(new
        {
            protocolVersion = 2,
            type = "targetSnapshot",
            connectionId,
            sequence = 3,
            target = new
            {
                bindingId = "page-binding",
                endpointId = "media-0",
                scope = "temporary",
                tabId = 7,
                frameId = 0,
                documentId = "document-1",
                pageOrigin = "https://example.com",
            },
            presentation = new
            {
                sourceDisplayName = "Big Buck Bunny — Brave",
                playbackStatus = "paused",
                playbackRate = 1.75,
                capabilities = new[] { "pause", "play", "seek", "toggle" },
                observedAt = "2026-08-27T00:00:01Z",
                timeline = new
                {
                    startSeconds = 0,
                    endSeconds = 600,
                    positionSeconds = 30,
                },
            },
        });
        var refreshed = await FirstAsync(
            adapter,
            value => value.ObservedTargets.SingleOrDefault()?.Presentation.PlaybackStatus ==
                PlaybackStatus.Paused);
        Assert.Equal(PlaybackStatus.Paused, Assert.Single(refreshed.ObservedTargets).Presentation.PlaybackStatus);

        var revoke = adapter.RevokeAsync(target.Id, CancellationToken.None).AsTask();
        var revokeRequest = await connection.ReadAsync();
        Assert.Equal("revoke", revokeRequest.GetProperty("type").GetString());
        var revokeRequestId = revokeRequest.GetProperty("requestId").GetString();
        await connection.WriteAsync(new
        {
            protocolVersion = 2,
            type = "targetRemoved",
            connectionId,
            sequence = 4,
            bindingId = "page-binding",
            reason = "permission-revoked",
        });
        await connection.WriteAsync(new
        {
            protocolVersion = 2,
            type = "revokeResult",
            connectionId,
            sequence = 5,
            requestId = revokeRequestId,
            revoked = true,
        });

        Assert.True(await revoke);
        var removed = await FirstAsync(adapter, value => value.ObservedTargets.Length == 0);
        Assert.Empty(removed.ObservedTargets);
    }

    private static async Task<MediaTargetCatalogSnapshot> FirstAsync(
        IMediaTargetCatalog catalog,
        Func<MediaTargetCatalogSnapshot, bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var snapshot in catalog.WatchAsync(timeout.Token))
        {
            if (predicate(snapshot))
            {
                return snapshot;
            }
        }

        throw new InvalidOperationException("The Browser Adapter did not publish the expected snapshot.");
    }

    private static string DeriveConnectionId(
        string hostNonce,
        string extensionNonce,
        string profileId)
    {
        var bytes = Encoding.UTF8.GetBytes(
            $"medialock.browser-direct.v2\n{ExtensionId}\n{hostNonce}\n{extensionNonce}\nbrave\n{profileId}\npause,play,seek,toggle");
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private sealed class TestConnection : IAsyncDisposable
    {
        private readonly NamedPipeServerStream server;
        private readonly NamedPipeClientStream client;
        private readonly Task adapterTask;

        private TestConnection(
            NamedPipeServerStream server,
            NamedPipeClientStream client,
            Task adapterTask)
        {
            this.server = server;
            this.client = client;
            this.adapterTask = adapterTask;
        }

        public static async Task<TestConnection> ConnectAsync(BrowserMediaAdapter adapter)
        {
            var pipeName = $"MediaLock.Browser.Tests.{Guid.NewGuid():N}";
            var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            var client = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            var accept = server.WaitForConnectionAsync();
            await client.ConnectAsync(5000);
            await accept;
            var adapterTask = adapter.RunConnectionAsync(server, CancellationToken.None);
            return new TestConnection(server, client, adapterTask);
        }

        public async Task<JsonElement> ReadAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var payload = await BrowserNativeMessageFrame.ReadAsync(client, timeout.Token);
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.Clone();
        }

        public async Task WriteAsync<T>(T value)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await BrowserNativeMessageFrame.WriteAsync(
                client,
                JsonSerializer.SerializeToUtf8Bytes(value),
                timeout.Token);
        }

        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            try
            {
                await adapterTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (IOException exception)
            {
                Assert.IsType<IOException>(exception);
            }
            server.Dispose();
        }
    }
}
