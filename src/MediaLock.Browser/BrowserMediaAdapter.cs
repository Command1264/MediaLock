using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using System.Diagnostics;
using MediaLock.Core.Media;

namespace MediaLock.Browser;

public sealed record BrowserMediaAdapterOptions
{
    public const string ProductionExtensionId = "kggfkkiifnclhhmibdglkbdfbacakemn";
    private static readonly Regex ExtensionIdPattern = new(
        "^[a-p]{32}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public BrowserMediaAdapterOptions(
        string extensionId,
        TimeSpan? commandTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(extensionId);
        if (!ExtensionIdPattern.IsMatch(extensionId) ||
            !string.Equals(extensionId, ProductionExtensionId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The Media Lock Chromium Extension ID is required.",
                nameof(extensionId));
        }

        var resolvedTimeout = commandTimeout ?? TimeSpan.FromSeconds(5);
        if (resolvedTimeout <= TimeSpan.Zero || resolvedTimeout > TimeSpan.FromSeconds(30))
        {
            throw new ArgumentOutOfRangeException(
                nameof(commandTimeout),
                resolvedTimeout,
                "The Browser command timeout must be greater than zero and at most 30 seconds.");
        }

        ExtensionId = extensionId;
        CommandTimeout = resolvedTimeout;
    }

    public string ExtensionId { get; }

    public TimeSpan CommandTimeout { get; }
}

public sealed class BrowserMediaAdapter :
    IMediaTargetCatalog,
    IMediaTargetController,
    IMediaTargetAuthorizationController
{
    private const int ProtocolVersion = 2;
    private static readonly ImmutableArray<string> HostCapabilities = ["pause", "play", "seek"];
    private static readonly HashSet<string> BrowserFamilies = new(StringComparer.Ordinal)
    {
        "brave",
        "chrome",
    };
    private static readonly HashSet<string> CommandErrorCodes = new(StringComparer.Ordinal)
    {
        "media-element-unavailable",
        "play-rejected",
        "seek-out-of-range",
        "target-unavailable",
        "unauthorized-command",
        "unsupported-command",
    };
    private readonly BrowserMediaAdapterOptions options;
    private readonly Channel<MediaTargetCatalogSnapshot> snapshots = Channel.CreateBounded<MediaTargetCatalogSnapshot>(
        new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.DropOldest,
        });
    private readonly CancellationTokenSource lifetime = new();
    private readonly object stateGate = new();
    private readonly Dictionary<MediaTargetId, BrowserTargetRegistration> targets = [];
    private int disposed;

    public BrowserMediaAdapter(BrowserMediaAdapterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.options = options;
        PublishSnapshot();
    }

    public IAsyncEnumerable<MediaTargetCatalogSnapshot> WatchAsync(
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        return snapshots.Reader.ReadAllAsync(cancellationToken);
    }

    public async Task RunConnectionAsync(
        Stream transport,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        using var connectionLifetime = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            lifetime.Token);
        var connection = new BrowserConnection(transport, options.CommandTimeout);
        try
        {
            var hostNonce = Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture);
            await connection.WriteAsync(new
            {
                protocolVersion = ProtocolVersion,
                type = "hostHello",
                hostNonce,
                capabilities = HostCapabilities,
            }, connectionLifetime.Token);

            var hello = await ReadDocumentAsync(transport, connectionLifetime.Token);
            var helloRoot = hello.RootElement;
            RequireExactProperties(
                helloRoot,
                "protocolVersion",
                "type",
                "hostNonce",
                "extensionNonce",
                "extensionId",
                "browserFamily",
                "profileId",
                "capabilities");
            RequireProtocolAndType(helloRoot, "extensionHello");
            if (!string.Equals(RequireString(helloRoot, "hostNonce"), hostNonce, StringComparison.Ordinal) ||
                !string.Equals(
                    RequireString(helloRoot, "extensionId"),
                    options.ExtensionId,
                    StringComparison.Ordinal))
            {
                throw new UnauthorizedAccessException("The Browser Extension identity is not authorized.");
            }

            var extensionNonce = RequireGuidText(helloRoot, "extensionNonce");
            var profileId = RequireGuidText(helloRoot, "profileId");
            var browserFamily = RequireString(helloRoot, "browserFamily");
            if (!BrowserFamilies.Contains(browserFamily))
            {
                throw new InvalidDataException("The Browser family is not supported.");
            }

            var capabilities = RequireCapabilities(helloRoot.GetProperty("capabilities"));
            var connectionId = DeriveConnectionId(
                options.ExtensionId,
                hostNonce,
                extensionNonce,
                browserFamily,
                profileId,
                capabilities);
            connection.Initialize(connectionId, profileId, browserFamily, capabilities);
            await connection.WriteAsync(new
            {
                protocolVersion = ProtocolVersion,
                type = "helloAck",
                hostNonce,
                extensionNonce,
                connectionId,
                browserFamily,
                profileId,
                capabilities,
            }, connectionLifetime.Token);

            while (!connectionLifetime.IsCancellationRequested)
            {
                using var message = await ReadDocumentAsync(transport, connectionLifetime.Token);
                HandleInbound(connection, message.RootElement);
            }
        }
        catch (EndOfStreamException exception) when (!connectionLifetime.IsCancellationRequested)
        {
            Trace.TraceInformation(
                "Browser integration connection closed: {0}",
                exception.GetType().Name);
        }
        catch (IOException exception) when (!connectionLifetime.IsCancellationRequested)
        {
            Trace.TraceWarning(
                "Browser integration transport failed: {0}",
                exception.GetType().Name);
        }
        finally
        {
            connection.Disconnect();
            RemoveConnectionTargets(connection);
            connection.Dispose();
        }
    }

    public ValueTask<MediaCommandOutcome> TryExecuteAsync(
        MediaTargetId target,
        MediaCommand command,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        BrowserTargetRegistration registration;
        lock (stateGate)
        {
            if (!targets.TryGetValue(target, out registration!))
            {
                return ValueTask.FromResult(MediaCommandOutcome.Rejected);
            }
        }

        if (!registration.Snapshot.Presentation.Capabilities.Supports(command))
        {
            return ValueTask.FromResult(MediaCommandOutcome.Unsupported);
        }

        return new ValueTask<MediaCommandOutcome>(registration.Connection.SendCommandAsync(
            registration,
            command,
            cancellationToken));
    }

    public async ValueTask<bool> RevokeAsync(
        MediaTargetId target,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        BrowserTargetRegistration registration;
        lock (stateGate)
        {
            if (!targets.TryGetValue(target, out registration!))
            {
                return false;
            }
        }

        return await registration.Connection.SendRevokeAsync(
            registration,
            cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        await lifetime.CancelAsync();
        snapshots.Writer.TryComplete();
        lifetime.Dispose();
    }

    private void HandleInbound(BrowserConnection connection, JsonElement root)
    {
        var type = RequireString(root, "type");
        switch (type)
        {
            case "targetSnapshot":
                HandleTargetSnapshot(connection, root);
                break;
            case "targetRemoved":
                HandleTargetRemoved(connection, root);
                break;
            case "commandResult":
                connection.HandleCommandResult(root);
                break;
            case "revokeResult":
                connection.HandleRevokeResult(root);
                break;
            default:
                throw new InvalidDataException("The Browser protocol message type is not allowed.");
        }
    }

    private void HandleTargetSnapshot(BrowserConnection connection, JsonElement root)
    {
        RequireExactProperties(
            root,
            "protocolVersion",
            "type",
            "connectionId",
            "sequence",
            "target",
            "presentation");
        connection.ValidateInboundEnvelope(root, "targetSnapshot");
        var endpoint = BrowserEndpoint.Parse(root.GetProperty("target"));
        var targetId = CreateTargetId(connection.ProfileId, endpoint.BindingId);
        var presentation = ParsePresentation(root.GetProperty("presentation"));
        var snapshot = MediaTargetSnapshot.FromProvider(targetId, presentation);
        lock (stateGate)
        {
            targets[targetId] = new BrowserTargetRegistration(
                connection,
                endpoint,
                snapshot);
        }

        PublishSnapshot();
    }

    private void HandleTargetRemoved(BrowserConnection connection, JsonElement root)
    {
        RequireExactProperties(
            root,
            "protocolVersion",
            "type",
            "connectionId",
            "sequence",
            "bindingId",
            "reason");
        connection.ValidateInboundEnvelope(root, "targetRemoved");
        var bindingId = RequireOpaque(root, "bindingId", 128);
        _ = RequireOpaque(root, "reason", 64);
        var targetId = CreateTargetId(connection.ProfileId, bindingId);
        lock (stateGate)
        {
            if (targets.TryGetValue(targetId, out var current) &&
                ReferenceEquals(current.Connection, connection))
            {
                targets.Remove(targetId);
            }
        }

        PublishSnapshot();
    }

    private void RemoveConnectionTargets(BrowserConnection connection)
    {
        var changed = false;
        lock (stateGate)
        {
            foreach (var target in targets
                .Where(pair => ReferenceEquals(pair.Value.Connection, connection))
                .Select(pair => pair.Key)
                .ToArray())
            {
                changed |= targets.Remove(target);
            }
        }

        if (changed)
        {
            PublishSnapshot();
        }
    }

    private void PublishSnapshot()
    {
        ImmutableArray<MediaTargetSnapshot> observed;
        lock (stateGate)
        {
            observed = targets.Values
                .Select(target => target.Snapshot)
                .OrderBy(target => target.Id.Value, StringComparer.Ordinal)
                .ToImmutableArray();
        }

        snapshots.Writer.TryWrite(new MediaTargetCatalogSnapshot(
            observed,
            null,
            [],
            MediaSessionCatalogStatus.Available));
    }

    private static MediaTargetPresentation ParsePresentation(JsonElement value)
    {
        RequireExactProperties(
            value,
            "sourceDisplayName",
            "playbackStatus",
            "capabilities",
            "observedAt",
            "timeline");
        var source = RequireBoundedString(value, "sourceDisplayName", 256);
        var playback = RequireString(value, "playbackStatus") switch
        {
            "playing" => PlaybackStatus.Playing,
            "paused" => PlaybackStatus.Paused,
            "stopped" => PlaybackStatus.Stopped,
            "changing" => PlaybackStatus.Changing,
            _ => PlaybackStatus.Unknown,
        };
        var capabilities = ToMediaCapabilities(RequireCapabilities(value.GetProperty("capabilities")));
        if (!DateTimeOffset.TryParse(
                RequireString(value, "observedAt"),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var observedAt))
        {
            throw new InvalidDataException("The Browser target observation timestamp is invalid.");
        }

        MediaTimeline? timeline = null;
        var timelineValue = value.GetProperty("timeline");
        if (timelineValue.ValueKind != JsonValueKind.Null)
        {
            RequireExactProperties(
                timelineValue,
                "startSeconds",
                "endSeconds",
                "positionSeconds");
            var start = RequireFiniteDouble(timelineValue, "startSeconds");
            var end = RequireFiniteDouble(timelineValue, "endSeconds");
            var position = RequireFiniteDouble(timelineValue, "positionSeconds");
            if (start < 0 || end <= start || position < start || position > end ||
                end > TimeSpan.MaxValue.TotalSeconds)
            {
                throw new InvalidDataException("The Browser target timeline is invalid.");
            }

            timeline = new MediaTimeline(
                TimeSpan.FromSeconds(start),
                TimeSpan.FromSeconds(end),
                TimeSpan.FromSeconds(position),
                observedAt);
        }

        return new MediaTargetPresentation(
            source,
            playback,
            capabilities,
            observedAt,
            Timeline: timeline);
    }

    private static MediaCommandCapabilities ToMediaCapabilities(IEnumerable<string> capabilities)
    {
        var result = MediaCommandCapabilities.None;
        foreach (var capability in capabilities)
        {
            result |= capability switch
            {
                "play" => MediaCommandCapabilities.Play,
                "pause" => MediaCommandCapabilities.Pause,
                "seek" => MediaCommandCapabilities.SeekAbsolute,
                _ => MediaCommandCapabilities.None,
            };
        }

        return result;
    }

    private static MediaTargetId CreateTargetId(string profileId, string bindingId)
    {
        var identity = Encoding.UTF8.GetBytes($"browser-page-binding\n{profileId}\n{bindingId}");
        return MediaTargetId.FromBrowserPageBinding(
            Convert.ToHexString(SHA256.HashData(identity)).ToLowerInvariant());
    }

    private static async ValueTask<JsonDocument> ReadDocumentAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var payload = await BrowserNativeMessageFrame.ReadAsync(stream, cancellationToken);
        try
        {
            return JsonDocument.Parse(payload, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8,
            });
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The Browser protocol message is malformed.", exception);
        }
    }

    private static string DeriveConnectionId(
        string extensionId,
        string hostNonce,
        string extensionNonce,
        string browserFamily,
        string profileId,
        IEnumerable<string> capabilities)
    {
        var canonicalCapabilities = string.Join(",", capabilities.Order(StringComparer.Ordinal));
        var value = Encoding.UTF8.GetBytes(
            $"medialock.browser-direct.v2\n{extensionId}\n{hostNonce}\n{extensionNonce}\n{browserFamily}\n{profileId}\n{canonicalCapabilities}");
        return Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    }

    private static ImmutableArray<string> RequireCapabilities(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Browser capabilities must be an array.");
        }

        var result = value.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String
                ? item.GetString()
                : null)
            .ToArray();
        if (result.Length is < 1 or > 3 || result.Any(item => item is null) ||
            result.Distinct(StringComparer.Ordinal).Count() != result.Length ||
            result.Any(item => !HostCapabilities.Contains(item!, StringComparer.Ordinal)))
        {
            throw new InvalidDataException("Browser capabilities are invalid or unsupported.");
        }

        return result.Select(item => item!).Order(StringComparer.Ordinal).ToImmutableArray();
    }

    private static void RequireProtocolAndType(JsonElement root, string type)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            root.GetProperty("protocolVersion").GetInt32() != ProtocolVersion ||
            !string.Equals(RequireString(root, "type"), type, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The Browser protocol version or message type is unsupported.");
        }
    }

    private static void RequireExactProperties(JsonElement value, params string[] expected)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("The Browser protocol value must be an object.");
        }

        var actual = value.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal);
        if (!actual.SequenceEqual(expected.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw new InvalidDataException("The Browser protocol object contains missing or unknown fields.");
        }
    }

    private static string RequireString(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String ||
            string.IsNullOrEmpty(property.GetString()))
        {
            throw new InvalidDataException($"Browser protocol field '{name}' must be a non-empty string.");
        }

        return property.GetString()!;
    }

    private static string RequireBoundedString(JsonElement value, string name, int maximumUtf8Bytes)
    {
        var result = RequireString(value, name);
        if (Encoding.UTF8.GetByteCount(result) > maximumUtf8Bytes)
        {
            throw new InvalidDataException($"Browser protocol field '{name}' is too large.");
        }

        return result;
    }

    private static string RequireOpaque(JsonElement value, string name, int maximumCharacters)
    {
        var result = RequireString(value, name);
        if (result.Length > maximumCharacters || result.Any(character => character is < '!' or > '~'))
        {
            throw new InvalidDataException($"Browser protocol field '{name}' is not a valid opaque identity.");
        }

        return result;
    }

    private static string RequireGuidText(JsonElement value, string name)
    {
        var result = RequireString(value, name);
        return Guid.TryParseExact(result, "D", out var parsed) && parsed != Guid.Empty
            ? result.ToLowerInvariant()
            : throw new InvalidDataException($"Browser protocol field '{name}' must be a non-empty UUID.");
    }

    private static double RequireFiniteDouble(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var property) ||
            property.ValueKind != JsonValueKind.Number ||
            !property.TryGetDouble(out var result) ||
            !double.IsFinite(result))
        {
            throw new InvalidDataException($"Browser protocol field '{name}' must be finite.");
        }

        return result;
    }

    private sealed record BrowserTargetRegistration(
        BrowserConnection Connection,
        BrowserEndpoint Endpoint,
        MediaTargetSnapshot Snapshot);

    private sealed record BrowserEndpoint(
        string BindingId,
        string EndpointId,
        string Scope,
        int TabId,
        int FrameId,
        string DocumentId,
        string PageOrigin)
    {
        public static BrowserEndpoint Parse(JsonElement value)
        {
            RequireExactProperties(
                value,
                "bindingId",
                "endpointId",
                "scope",
                "tabId",
                "frameId",
                "documentId",
                "pageOrigin");
            var scope = RequireString(value, "scope");
            var pageOrigin = RequireString(value, "pageOrigin");
            if (scope is not ("temporary" or "site") ||
                !Uri.TryCreate(pageOrigin, UriKind.Absolute, out var origin) ||
                origin.Scheme != Uri.UriSchemeHttps ||
                origin.AbsoluteUri.TrimEnd('/') != pageOrigin)
            {
                throw new UnauthorizedAccessException("The Browser target authorization scope is invalid.");
            }

            var tabId = value.GetProperty("tabId").GetInt32();
            var frameId = value.GetProperty("frameId").GetInt32();
            if (tabId < 0 || frameId != 0)
            {
                throw new UnauthorizedAccessException("Only a valid top-frame Browser target is supported.");
            }

            return new BrowserEndpoint(
                RequireOpaque(value, "bindingId", 128),
                RequireOpaque(value, "endpointId", 128),
                scope,
                tabId,
                frameId,
                RequireOpaque(value, "documentId", 256),
                pageOrigin);
        }
    }

    private sealed class BrowserConnection : IDisposable
    {
        private readonly Stream transport;
        private readonly TimeSpan commandTimeout;
        private readonly SemaphoreSlim writeGate = new(1, 1);
        private readonly object pendingGate = new();
        private readonly Dictionary<Guid, TaskCompletionSource<MediaCommandOutcome>> pending = [];
        private ImmutableHashSet<string> capabilities = [];
        private string connectionId = string.Empty;
        private int inboundSequence;
        private int outboundSequence;
        private int connected = 1;

        public BrowserConnection(Stream transport, TimeSpan commandTimeout)
        {
            this.transport = transport;
            this.commandTimeout = commandTimeout;
        }

        public string ProfileId { get; private set; } = string.Empty;

        public void Initialize(
            string resolvedConnectionId,
            string profileId,
            string browserFamily,
            IEnumerable<string> negotiatedCapabilities)
        {
            connectionId = resolvedConnectionId;
            ProfileId = profileId;
            capabilities = negotiatedCapabilities.ToImmutableHashSet(StringComparer.Ordinal);
        }

        public async Task<MediaCommandOutcome> SendCommandAsync(
            BrowserTargetRegistration registration,
            MediaCommand command,
            CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref connected) == 0)
            {
                return MediaCommandOutcome.Rejected;
            }

            var commandName = command.Kind switch
            {
                MediaCommandKind.Play => "play",
                MediaCommandKind.Pause => "pause",
                MediaCommandKind.SeekAbsolute => "seek",
                _ => null,
            };
            if (commandName is null || !capabilities.Contains(commandName))
            {
                return MediaCommandOutcome.Unsupported;
            }

            var requestId = Guid.NewGuid();
            var completion = new TaskCompletionSource<MediaCommandOutcome>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            lock (pendingGate)
            {
                if (pending.Count >= 64)
                {
                    return MediaCommandOutcome.Rejected;
                }

                pending.Add(requestId, completion);
            }

            try
            {
                object commandValue = commandName == "seek"
                    ? new
                    {
                        name = commandName,
                        positionSeconds = command.AbsolutePosition!.Value.TotalSeconds,
                    }
                    : new { name = commandName };
                await WriteAsync(new
                {
                    protocolVersion = ProtocolVersion,
                    type = "command",
                    connectionId,
                    sequence = Interlocked.Increment(ref outboundSequence),
                    requestId,
                    target = registration.Endpoint,
                    command = commandValue,
                }, cancellationToken);

                return await completion.Task.WaitAsync(commandTimeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                return MediaCommandOutcome.OutcomeUnknown;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception) when (Volatile.Read(ref connected) == 0)
            {
                return MediaCommandOutcome.OutcomeUnknown;
            }
            finally
            {
                lock (pendingGate)
                {
                    pending.Remove(requestId);
                }
            }
        }

        public async Task<bool> SendRevokeAsync(
            BrowserTargetRegistration registration,
            CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref connected) == 0)
            {
                return false;
            }

            var requestId = Guid.NewGuid();
            var completion = new TaskCompletionSource<MediaCommandOutcome>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            lock (pendingGate)
            {
                if (pending.Count >= 64)
                {
                    return false;
                }

                pending.Add(requestId, completion);
            }

            try
            {
                await WriteAsync(new
                {
                    protocolVersion = ProtocolVersion,
                    type = "revoke",
                    connectionId,
                    sequence = Interlocked.Increment(ref outboundSequence),
                    requestId,
                    bindingId = registration.Endpoint.BindingId,
                }, cancellationToken);
                return await completion.Task.WaitAsync(commandTimeout, cancellationToken) ==
                    MediaCommandOutcome.Succeeded;
            }
            catch (TimeoutException)
            {
                return false;
            }
            finally
            {
                lock (pendingGate)
                {
                    pending.Remove(requestId);
                }
            }
        }

        public void HandleCommandResult(JsonElement root)
        {
            RequireExactProperties(
                root,
                "protocolVersion",
                "type",
                "connectionId",
                "sequence",
                "requestId",
                "accepted",
                "errorCode");
            ValidateInboundEnvelope(root, "commandResult");
            if (!Guid.TryParseExact(RequireString(root, "requestId"), "D", out var requestId))
            {
                throw new InvalidDataException("The Browser command result request ID is invalid.");
            }

            TaskCompletionSource<MediaCommandOutcome>? completion;
            lock (pendingGate)
            {
                pending.TryGetValue(requestId, out completion);
            }
            if (completion is null)
            {
                throw new InvalidDataException("The Browser command result is stale or unknown.");
            }

            var accepted = root.GetProperty("accepted").ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw new InvalidDataException("The Browser command result acceptance is invalid."),
            };
            var error = root.GetProperty("errorCode");
            if (accepted && error.ValueKind != JsonValueKind.Null)
            {
                throw new InvalidDataException("An accepted Browser command cannot contain an error code.");
            }
            if (!accepted && error.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException("A rejected Browser command requires an error code.");
            }
            if (!accepted && !CommandErrorCodes.Contains(error.GetString()!))
            {
                throw new InvalidDataException("The Browser command result error code is not allowed.");
            }

            completion.TrySetResult(accepted
                ? MediaCommandOutcome.Succeeded
                : MediaCommandOutcome.Rejected);
        }

        public void HandleRevokeResult(JsonElement root)
        {
            RequireExactProperties(
                root,
                "protocolVersion",
                "type",
                "connectionId",
                "sequence",
                "requestId",
                "revoked");
            ValidateInboundEnvelope(root, "revokeResult");
            if (!Guid.TryParseExact(RequireString(root, "requestId"), "D", out var requestId))
            {
                throw new InvalidDataException("The Browser revoke result request ID is invalid.");
            }

            TaskCompletionSource<MediaCommandOutcome>? completion;
            lock (pendingGate)
            {
                pending.TryGetValue(requestId, out completion);
            }
            if (completion is null)
            {
                throw new InvalidDataException("The Browser revoke result is stale or unknown.");
            }

            var revoked = root.GetProperty("revoked").ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw new InvalidDataException("The Browser revoke result is invalid."),
            };
            completion.TrySetResult(revoked
                ? MediaCommandOutcome.Succeeded
                : MediaCommandOutcome.Rejected);
        }

        public void ValidateInboundEnvelope(JsonElement root, string type)
        {
            RequireProtocolAndType(root, type);
            if (!string.Equals(
                    RequireString(root, "connectionId"),
                    connectionId,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("The Browser protocol connection identity is stale.");
            }

            var sequence = root.GetProperty("sequence").GetInt32();
            if (sequence != inboundSequence + 1)
            {
                throw new InvalidDataException("The Browser protocol sequence is not strictly monotonic.");
            }

            inboundSequence = sequence;
        }

        public async ValueTask WriteAsync<T>(T value, CancellationToken cancellationToken)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(value, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });
            await writeGate.WaitAsync(cancellationToken);
            try
            {
                await BrowserNativeMessageFrame.WriteAsync(transport, payload, cancellationToken);
            }
            finally
            {
                writeGate.Release();
            }
        }

        public void Disconnect()
        {
            if (Interlocked.Exchange(ref connected, 0) == 0)
            {
                return;
            }

            lock (pendingGate)
            {
                foreach (var completion in pending.Values)
                {
                    completion.TrySetResult(MediaCommandOutcome.OutcomeUnknown);
                }
            }
        }

        public void Dispose() => writeGate.Dispose();
    }
}
