using System.Text.Json;
using MediaLock.Phase16ABrowserDirectProbe;

namespace Phase16A.BrowserDirectProbe.Tests;

public sealed class BrowserDirectProtocolSessionTests
{
    private const string ExtensionId = "abcdefghijklmnopabcdefghijklmnop";
    private static readonly Guid SessionId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid ExtensionNonce = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private const string ConnectionId = "2e9cfd93293ebe38ff70df4df25986f814b6c1b1ee52d345aa558bb3884f5223";
    private static readonly Guid RequestId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private const string DocumentId = "ABCDEF0123456789ABCDEF0123456789";

    [Fact]
    public void CreateHello_BindsTheProtocolToOneFreshSession()
    {
        var session = new BrowserDirectProtocolSession(ExtensionId, SessionId);

        using var hello = JsonDocument.Parse(session.CreateHello());

        Assert.Equal(1, hello.RootElement.GetProperty("protocolVersion").GetInt32());
        Assert.Equal("hostHello", hello.RootElement.GetProperty("type").GetString());
        Assert.Equal(SessionId, hello.RootElement.GetProperty("hostNonce").GetGuid());
    }

    [Fact]
    public void Handle_RequiresExactExtensionHelloBeforeAnyCommand()
    {
        var session = new BrowserDirectProtocolSession(ExtensionId, SessionId);

        Assert.Throws<InvalidDataException>(() => session.Handle(CreateProbeRequest()));
        Assert.Throws<UnauthorizedAccessException>(() => session.Handle(CreateExtensionHello("ponmlkjihgfedcbaponmlkjihgfedcba")));

        var acknowledgement = session.Handle(CreateExtensionHello(ExtensionId));
        Assert.NotNull(acknowledgement);
        using var parsed = JsonDocument.Parse(acknowledgement);
        Assert.Equal("helloAck", parsed.RootElement.GetProperty("type").GetString());
        Assert.Equal(ConnectionId, parsed.RootElement.GetProperty("connectionId").GetString());
    }

    [Theory]
    [InlineData("edge", "pause")]
    [InlineData("brave", "run-script")]
    public void Handle_RejectsUnsupportedBrowserFamilyOrCapability(string browserFamily, string capability)
    {
        var session = new BrowserDirectProtocolSession(ExtensionId, SessionId);

        Assert.Throws<InvalidDataException>(() => session.Handle(CreateExtensionHello(
            ExtensionId,
            browserFamily,
            capability)));
    }

    [Fact]
    public void Handle_RejectsACommandOutsideTheNegotiatedCapabilities()
    {
        var session = new BrowserDirectProtocolSession(ExtensionId, SessionId);
        var acknowledgement = session.Handle(CreateExtensionHello(ExtensionId, onlyCapability: "pause"));
        Assert.NotNull(acknowledgement);
        using var parsed = JsonDocument.Parse(acknowledgement);
        var negotiatedConnectionId = parsed.RootElement.GetProperty("connectionId").GetString();

        Assert.Throws<InvalidDataException>(() => session.Handle(CreateProbeRequest(
            commandName: "play",
            connectionId: negotiatedConnectionId)));
    }

    [Fact]
    public void Handle_ReturnsOneCorrelatedAllowlistedCommand()
    {
        var session = CreateConnectedSession();

        var response = session.Handle(CreateProbeRequest());

        Assert.NotNull(response);
        using var parsed = JsonDocument.Parse(response);
        var root = parsed.RootElement;
        Assert.Equal("command", root.GetProperty("type").GetString());
        Assert.Equal(1, root.GetProperty("sequence").GetInt32());
        Assert.Equal(RequestId, root.GetProperty("requestId").GetGuid());
        Assert.Equal(42, root.GetProperty("target").GetProperty("tabId").GetInt32());
        Assert.Equal(DocumentId, root.GetProperty("target").GetProperty("documentId").GetString());
        Assert.Equal("pause", root.GetProperty("command").GetProperty("name").GetString());
    }

    [Fact]
    public void Handle_RejectsReplayAndOutOfOrderRequests()
    {
        var session = CreateConnectedSession();
        _ = session.Handle(CreateProbeRequest());

        Assert.Throws<InvalidDataException>(() => session.Handle(CreateProbeRequest(sequence: 1, requestId: Guid.NewGuid())));
        Assert.Throws<InvalidDataException>(() => session.Handle(CreateProbeRequest(sequence: 2)));
    }

    [Theory]
    [InlineData("https://example.com", 0, "pause")]
    [InlineData("https://music.youtube.com", 1, "pause")]
    [InlineData("https://music.youtube.com", 0, "run-script")]
    public void Handle_RejectsUnauthorizedTargetsAndCommands(string pageOrigin, int frameId, string command)
    {
        var session = CreateConnectedSession();

        Assert.ThrowsAny<Exception>(() => session.Handle(CreateProbeRequest(
            pageOrigin: pageOrigin,
            frameId: frameId,
            commandName: command)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("line\nbreak")]
    public void Handle_RejectsMalformedOpaqueDocumentIds(string documentId)
    {
        var session = CreateConnectedSession();

        Assert.Throws<InvalidDataException>(() => session.Handle(CreateProbeRequest(documentId: documentId)));
    }

    [Fact]
    public void Handle_RejectsAnOverlongOpaqueDocumentId()
    {
        var session = CreateConnectedSession();

        Assert.Throws<InvalidDataException>(() => session.Handle(CreateProbeRequest(documentId: new string('x', 257))));
    }

    [Fact]
    public void Handle_RejectsUnknownFields()
    {
        var session = CreateConnectedSession();
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            protocolVersion = 1,
            type = "probeRequest",
            connectionId = ConnectionId,
            sequence = 1,
            requestId = RequestId,
            target = new { tabId = 42, frameId = 0, documentId = DocumentId, pageOrigin = "https://music.youtube.com" },
            command = new { name = "pause" },
            arbitraryScript = "alert(1)",
        });

        Assert.Throws<InvalidDataException>(() => session.Handle(payload));
    }

    [Fact]
    public void Handle_AcceptsOneCorrelatedResultAndRejectsUnknownOrDuplicateResults()
    {
        var session = CreateConnectedSession();
        _ = session.Handle(CreateProbeRequest());

        Assert.Null(session.Handle(CreateCommandResult(sequence: 2, RequestId)));
        Assert.Throws<InvalidDataException>(() => session.Handle(CreateCommandResult(sequence: 3, RequestId)));

        var secondSession = CreateConnectedSession();
        Assert.Throws<InvalidDataException>(() => secondSession.Handle(CreateCommandResult(sequence: 1, Guid.NewGuid())));
    }

    [Fact]
    public void Handle_RejectsRequestsBeyondThePendingCommandLimitBeforeStateMutation()
    {
        var session = CreateConnectedSession();
        for (var sequence = 1; sequence <= 64; sequence++)
        {
            _ = session.Handle(CreateProbeRequest(sequence, CreateGuid(sequence)));
        }

        Assert.Throws<InvalidDataException>(() => session.Handle(CreateProbeRequest(65, CreateGuid(65))));
    }

    private static Guid CreateGuid(int value)
    {
        return Guid.Parse($"{value:x8}-0000-4000-8000-000000000000");
    }

    private static BrowserDirectProtocolSession CreateConnectedSession()
    {
        var session = new BrowserDirectProtocolSession(ExtensionId, SessionId);
        _ = session.Handle(CreateExtensionHello(ExtensionId));
        return session;
    }

    private static byte[] CreateExtensionHello(
        string extensionId,
        string browserFamily = "brave",
        string? onlyCapability = null)
    {
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            protocolVersion = 1,
            type = "extensionHello",
            hostNonce = SessionId,
            extensionNonce = ExtensionNonce,
            extensionId,
            browserFamily,
            capabilities = onlyCapability is null ? ["pause", "play", "seek"] : new[] { onlyCapability },
        });
    }

    private static byte[] CreateProbeRequest(
        int sequence = 1,
        Guid? requestId = null,
        string pageOrigin = "https://music.youtube.com",
        int frameId = 0,
        string commandName = "pause",
        string? connectionId = null,
        string? documentId = null)
    {
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            protocolVersion = 1,
            type = "probeRequest",
            connectionId = connectionId ?? ConnectionId,
            sequence,
            requestId = requestId ?? RequestId,
            target = new { tabId = 42, frameId, documentId = documentId ?? DocumentId, pageOrigin },
            command = new { name = commandName },
        });
    }

    private static byte[] CreateCommandResult(int sequence, Guid requestId)
    {
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            protocolVersion = 1,
            type = "commandResult",
            connectionId = ConnectionId,
            sequence,
            requestId,
            accepted = true,
            errorCode = (string?)null,
        });
    }
}
