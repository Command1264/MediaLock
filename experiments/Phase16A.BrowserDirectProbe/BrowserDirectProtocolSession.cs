using System.Text.Json;

namespace MediaLock.Phase16ABrowserDirectProbe;

public sealed class BrowserDirectProtocolSession
{
    private const int ProtocolVersion = 1;
    private const int MaximumRememberedRequestIds = 1024;
    private static readonly HashSet<string> AllowedPageOrigins = new(StringComparer.Ordinal)
    {
        "https://www.youtube.com",
        "https://music.youtube.com",
    };
    private static readonly HashSet<string> AllowedCommands = new(StringComparer.Ordinal)
    {
        "play",
        "pause",
        "seek",
    };
    private static readonly HashSet<string> AllowedResultErrors = new(StringComparer.Ordinal)
    {
        "media-element-unavailable",
        "seek-out-of-range",
        "play-rejected",
        "unauthorized-command",
        "target-unavailable",
    };

    private readonly string extensionId;
    private readonly Guid sessionId;
    private readonly HashSet<Guid> observedRequestIds = [];
    private readonly Queue<Guid> requestIdOrder = [];
    private readonly HashSet<Guid> pendingRequestIds = [];
    private bool extensionConnected;
    private int inboundSequence;
    private int outboundSequence;

    public BrowserDirectProtocolSession(string extensionId, Guid sessionId)
    {
        _ = NativeHostOrigin.Validate($"chrome-extension://{extensionId}/", extensionId);
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("The protocol session ID must not be empty.", nameof(sessionId));
        }

        this.extensionId = extensionId;
        this.sessionId = sessionId;
    }

    public byte[] CreateHello()
    {
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            protocolVersion = ProtocolVersion,
            type = "hostHello",
            sessionId,
        });
    }

    public byte[]? Handle(ReadOnlyMemory<byte> payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8,
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("The protocol envelope must be a JSON object.");
            }

            var type = RequireString(root, "type");
            return type switch
            {
                "extensionHello" => HandleExtensionHello(root),
                "probeRequest" => HandleProbeRequest(root),
                "commandResult" => HandleCommandResult(root),
                _ => throw new InvalidDataException("The protocol message type is not allowed."),
            };
        }
        catch (Exception exception) when (exception is InvalidDataException or UnauthorizedAccessException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException)
        {
            throw new InvalidDataException("The protocol message is malformed.", exception);
        }
    }

    private byte[] HandleExtensionHello(JsonElement root)
    {
        RequireExactProperties(root, "protocolVersion", "type", "sessionId", "extensionId");
        RequireProtocolAndSession(root);
        if (extensionConnected)
        {
            throw new InvalidDataException("The Extension hello has already been accepted.");
        }

        var claimedExtensionId = RequireString(root, "extensionId");
        if (!string.Equals(claimedExtensionId, extensionId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("The Extension hello identity is not authorized.");
        }

        extensionConnected = true;
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            protocolVersion = ProtocolVersion,
            type = "helloAck",
            sessionId,
        });
    }

    private byte[] HandleProbeRequest(JsonElement root)
    {
        if (!extensionConnected)
        {
            throw new InvalidDataException("The Extension hello must be accepted before commands.");
        }

        RequireExactProperties(
            root,
            "protocolVersion",
            "type",
            "sessionId",
            "sequence",
            "requestId",
            "target",
            "command");
        RequireProtocolAndSession(root);

        var sequence = RequireInt32(root, "sequence");
        if (sequence != inboundSequence + 1)
        {
            throw new InvalidDataException("The inbound sequence is not strictly monotonic.");
        }

        var requestId = RequireGuid(root, "requestId");
        if (observedRequestIds.Contains(requestId))
        {
            throw new InvalidDataException("The request ID was already observed in this protocol session.");
        }

        var target = RequireObject(root, "target");
        RequireExactProperties(target, "tabId", "frameId", "pageOrigin");
        var tabId = RequireInt32(target, "tabId");
        var frameId = RequireInt32(target, "frameId");
        var pageOrigin = RequireString(target, "pageOrigin");
        if (tabId < 0 || frameId != 0 || !AllowedPageOrigins.Contains(pageOrigin))
        {
            throw new UnauthorizedAccessException("The requested browser target is not authorized.");
        }

        var command = RequireObject(root, "command");
        var commandName = RequireString(command, "name");
        if (!AllowedCommands.Contains(commandName))
        {
            throw new InvalidDataException("The requested media command is not allowed.");
        }

        double? positionSeconds = null;
        if (commandName == "seek")
        {
            RequireExactProperties(command, "name", "positionSeconds");
            positionSeconds = RequireDouble(command, "positionSeconds");
            if (!double.IsFinite(positionSeconds.Value) || positionSeconds.Value < 0)
            {
                throw new InvalidDataException("Seek position must be finite and non-negative.");
            }
        }
        else
        {
            RequireExactProperties(command, "name");
        }

        inboundSequence = sequence;
        RememberRequestId(requestId);
        pendingRequestIds.Add(requestId);
        outboundSequence++;
        var response = positionSeconds is null
            ? new
            {
                protocolVersion = ProtocolVersion,
                type = "command",
                sessionId,
                sequence = outboundSequence,
                requestId,
                target = new { tabId, frameId, pageOrigin },
                command = new { name = commandName },
            }
            : null;
        if (response is not null)
        {
            return JsonSerializer.SerializeToUtf8Bytes(response);
        }

        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            protocolVersion = ProtocolVersion,
            type = "command",
            sessionId,
            sequence = outboundSequence,
            requestId,
            target = new { tabId, frameId, pageOrigin },
            command = new { name = commandName, positionSeconds = positionSeconds!.Value },
        });
    }

    private byte[]? HandleCommandResult(JsonElement root)
    {
        if (!extensionConnected)
        {
            throw new InvalidDataException("The Extension hello must be accepted before command results.");
        }

        RequireExactProperties(
            root,
            "protocolVersion",
            "type",
            "sessionId",
            "sequence",
            "requestId",
            "accepted",
            "errorCode");
        RequireProtocolAndSession(root);
        var sequence = RequireInt32(root, "sequence");
        if (sequence != inboundSequence + 1)
        {
            throw new InvalidDataException("The inbound sequence is not strictly monotonic.");
        }

        var requestId = RequireGuid(root, "requestId");
        if (!pendingRequestIds.Contains(requestId))
        {
            throw new InvalidDataException("The command result does not match a pending request.");
        }

        var acceptedProperty = root.GetProperty("accepted");
        if (acceptedProperty.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException("Protocol field 'accepted' must be a boolean.");
        }
        var accepted = acceptedProperty.GetBoolean();
        var errorProperty = root.GetProperty("errorCode");
        string? errorCode = errorProperty.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => errorProperty.GetString(),
            _ => throw new InvalidDataException("Protocol field 'errorCode' must be a string or null."),
        };
        if ((accepted && errorCode is not null)
            || (!accepted && (errorCode is null || !AllowedResultErrors.Contains(errorCode))))
        {
            throw new InvalidDataException("The command result status is inconsistent or unsupported.");
        }

        inboundSequence = sequence;
        pendingRequestIds.Remove(requestId);
        return null;
    }

    private void RequireProtocolAndSession(JsonElement root)
    {
        if (RequireInt32(root, "protocolVersion") != ProtocolVersion)
        {
            throw new InvalidDataException("The protocol version is not supported.");
        }

        if (RequireGuid(root, "sessionId") != sessionId)
        {
            throw new InvalidDataException("The message belongs to a stale protocol session.");
        }
    }

    private void RememberRequestId(Guid requestId)
    {
        observedRequestIds.Add(requestId);
        requestIdOrder.Enqueue(requestId);
        if (requestIdOrder.Count > MaximumRememberedRequestIds)
        {
            observedRequestIds.Remove(requestIdOrder.Dequeue());
        }
    }

    private static void RequireExactProperties(JsonElement value, params string[] expectedNames)
    {
        var actualNames = value.EnumerateObject().Select(property => property.Name).ToArray();
        if (actualNames.Length != expectedNames.Length
            || expectedNames.Any(expected => !actualNames.Contains(expected, StringComparer.Ordinal)))
        {
            throw new InvalidDataException("The protocol message contains a missing, duplicate or unknown field.");
        }
    }

    private static JsonElement RequireObject(JsonElement value, string propertyName)
    {
        var property = value.GetProperty(propertyName);
        if (property.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"Protocol field '{propertyName}' must be an object.");
        }

        return property;
    }

    private static string RequireString(JsonElement value, string propertyName)
    {
        var property = value.GetProperty(propertyName);
        if (property.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException($"Protocol field '{propertyName}' must be a string.");
        }

        return property.GetString()!;
    }

    private static Guid RequireGuid(JsonElement value, string propertyName)
    {
        var text = RequireString(value, propertyName);
        if (!Guid.TryParseExact(text, "D", out var result) || result == Guid.Empty)
        {
            throw new InvalidDataException($"Protocol field '{propertyName}' must be a non-empty UUID.");
        }

        return result;
    }

    private static int RequireInt32(JsonElement value, string propertyName)
    {
        var property = value.GetProperty(propertyName);
        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out var result))
        {
            throw new InvalidDataException($"Protocol field '{propertyName}' must be a 32-bit integer.");
        }

        return result;
    }

    private static double RequireDouble(JsonElement value, string propertyName)
    {
        var property = value.GetProperty(propertyName);
        if (property.ValueKind != JsonValueKind.Number || !property.TryGetDouble(out var result))
        {
            throw new InvalidDataException($"Protocol field '{propertyName}' must be a number.");
        }

        return result;
    }
}
