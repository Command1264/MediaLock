using System.IO.Pipes;
using MediaLock.Browser;
using MediaLock.BrowserHost;

try
{
    if (args.Length < 1)
    {
        throw new UnauthorizedAccessException("The browser did not provide a Native Messaging launch origin.");
    }

    var executablePath = Environment.ProcessPath
        ?? throw new InvalidOperationException("The Browser Host executable path is unavailable.");
    var configuration = BrowserHostConfiguration.Load(executablePath);
    configuration.ValidateLaunchOrigin(args[0]);

    await using var pipe = new NamedPipeClientStream(
        ".",
        configuration.PipeName,
        PipeDirection.InOut,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    using var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    await pipe.ConnectAsync(connectTimeout.Token);

    using var lifetime = new CancellationTokenSource();
    var browserToApp = RelayAsync(
        Console.OpenStandardInput(),
        pipe,
        lifetime.Token);
    var appToBrowser = RelayAsync(
        pipe,
        Console.OpenStandardOutput(),
        lifetime.Token);
    await Task.WhenAny(browserToApp, appToBrowser);
    await lifetime.CancelAsync();
    await Task.WhenAll(browserToApp, appToBrowser);
    return 0;
}
catch (OperationCanceledException)
{
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(
        $"Media Lock Browser Host rejected the connection: {exception.GetType().Name}");
    return 1;
}

static async Task RelayAsync(
    Stream input,
    Stream output,
    CancellationToken cancellationToken)
{
    while (!cancellationToken.IsCancellationRequested)
    {
        var payload = await BrowserNativeMessageFrame.ReadAsync(input, cancellationToken);
        await BrowserNativeMessageFrame.WriteAsync(output, payload, cancellationToken);
    }
}
