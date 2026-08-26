using MediaLock.Phase16ABrowserDirectProbe;

const int maximumInboundPayloadBytes = 64 * 1024;
const int maximumOutboundPayloadBytes = 64 * 1024;

try
{
    if (args.Length < 1)
    {
        throw new UnauthorizedAccessException("The browser did not provide a Native Messaging launch origin.");
    }

    var executablePath = Environment.ProcessPath
        ?? throw new InvalidOperationException("The Native Host executable path is unavailable.");
    var configuration = NativeHostConfiguration.Load(executablePath);
    _ = NativeHostOrigin.Validate(args[0], configuration.ExtensionId);

    var protocolSession = new BrowserDirectProtocolSession(configuration.ExtensionId, Guid.NewGuid());
    var hello = protocolSession.CreateHello();
    await NativeMessagingFrame.WriteAsync(
        Console.OpenStandardOutput(),
        hello,
        maximumOutboundPayloadBytes,
        CancellationToken.None);

    while (true)
    {
        var payload = await NativeMessagingFrame.TryReadAsync(
            Console.OpenStandardInput(),
            maximumInboundPayloadBytes,
            CancellationToken.None);
        if (payload is null)
        {
            return 0;
        }

        var response = protocolSession.Handle(payload);
        if (response is not null)
        {
            await NativeHostCommandDelay.ApplyAsync(
                response,
                configuration.CommandResponseDelayMilliseconds,
                Task.Delay,
                CancellationToken.None);
            await NativeMessagingFrame.WriteAsync(
                Console.OpenStandardOutput(),
                response,
                maximumOutboundPayloadBytes,
                CancellationToken.None);
        }
    }
}
catch (Exception exception) when (exception is not OperationCanceledException)
{
    Console.Error.WriteLine($"Phase 16A Native Host rejected the connection: {exception.GetType().Name}: {exception.Message}");
    return 1;
}
