using System.Threading.Channels;

namespace MediaLock.Probe;

internal static class ProbeApplication
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 1 && args[0] is "--help" or "-h")
        {
            PrintHelp();
            return 0;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        await using var intentQueue = new SerializedIntentQueue();
        intentQueue.UnhandledException += exception =>
            ConsoleLog.Error($"Serialized intent failed: {exception.Message}");
        using var lifecycle = new SystemLifecycle();
        using var sessions = new GsmtcSessionService(
            intentQueue,
            new GsmtcSessionManagerFactory(),
            lifecycle,
            TimeProvider.System);
        sessions.StateChanged += ConsoleLog.Info;

        try
        {
            await intentQueue.InvokeAsync(sessions.InitializeAsync);
        }
        catch (Exception exception)
        {
            ConsoleLog.Error($"Unable to initialize GSMTC: {exception.Message}");
            return 1;
        }

        var inputQueue = Channel.CreateBounded<MediaKeyInput>(new BoundedChannelOptions(128)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        });

        var hook = new LowLevelMediaKeyHook(command =>
        {
            if (!sessions.TryPrepareRoute(command, out var route, out var reason))
            {
                inputQueue.Writer.TryWrite(new MediaKeyInput(command, null, reason));
                return false;
            }

            return inputQueue.Writer.TryWrite(new MediaKeyInput(command, route, reason));
        });

        var inputWorker = ProcessInputsAsync(inputQueue.Reader);

        ConsoleLog.Info("Media Lock Phase 0 probe. Type 'help' for commands.");
        await intentQueue.InvokeAsync(sessions.PrintSessionsAsync);

        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                Console.Write("media-lock> ");
                var line = await Console.In.ReadLineAsync(cancellation.Token);
                if (line is null)
                {
                    break;
                }

                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length == 0)
                {
                    continue;
                }

                var command = parts[0].ToLowerInvariant();
                switch (command)
                {
                    case "help":
                        PrintHelp();
                        break;
                    case "list":
                    case "refresh":
                        await intentQueue.InvokeAsync(sessions.PrintSessionsAsync);
                        break;
                    case "select":
                        await SelectSessionAsync(parts, sessions, intentQueue);
                        break;
                    case "clear":
                        await intentQueue.InvokeAsync(sessions.ClearSelection);
                        break;
                    case "status":
                        await intentQueue.InvokeAsync(sessions.PrintSelectedStatus);
                        ConsoleLog.Info($"Keyboard hook: {(hook.IsRunning ? "on" : "off")}");
                        break;
                    case "hook":
                        await ConfigureHookAsync(parts, hook);
                        break;
                    case "play":
                    case "pause":
                    case "toggle":
                    case "next":
                    case "previous":
                    case "stop":
                        var result = await intentQueue.InvokeAsync(() => sessions.ExecuteAsync(command));
                        LogResult(result);
                        break;
                    case "quit":
                    case "exit":
                        cancellation.Cancel();
                        break;
                    default:
                        ConsoleLog.Error($"Unknown command '{command}'. Type 'help'.");
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            hook.Dispose();
            inputQueue.Writer.TryComplete();
            await inputWorker;
        }

        return 0;
    }

    private static async Task ProcessInputsAsync(ChannelReader<MediaKeyInput> reader)
    {
        await foreach (var input in reader.ReadAllAsync())
        {
            if (input.Route is null)
            {
                ConsoleLog.Info($"INPUT {input.Command}; pass-through ({input.Reason}).");
                continue;
            }

            ConsoleLog.Info($"INPUT {input.Command}; consumed; queued for selected session.");
            var result = await GsmtcSessionService.RouteAsync(input.Route);
            LogResult(result);
        }
    }

    private static async Task SelectSessionAsync(
        string[] parts,
        GsmtcSessionService sessions,
        SerializedIntentQueue intentQueue)
    {
        if (parts.Length != 2 || !int.TryParse(parts[1], out var oneBasedIndex))
        {
            ConsoleLog.Error("Usage: select <session-number>");
            return;
        }

        await intentQueue.InvokeAsync(() =>
        {
            if (sessions.Select(oneBasedIndex - 1, out var message))
            {
                ConsoleLog.Info(message);
            }
            else
            {
                ConsoleLog.Error(message);
            }
        });
    }

    private static async Task ConfigureHookAsync(string[] parts, LowLevelMediaKeyHook hook)
    {
        if (parts.Length != 2 || parts[1] is not ("on" or "off"))
        {
            ConsoleLog.Error("Usage: hook <on|off>");
            return;
        }

        try
        {
            if (parts[1] == "on")
            {
                await hook.StartAsync();
                ConsoleLog.Info("Keyboard hook enabled. Routable media keys will be consumed.");
            }
            else
            {
                hook.Stop();
                ConsoleLog.Info("Keyboard hook disabled.");
            }
        }
        catch (Exception exception)
        {
            ConsoleLog.Error($"Unable to configure keyboard hook: {exception.Message}");
        }
    }

    private static void LogResult((bool Success, string Message) result)
    {
        if (result.Success)
        {
            ConsoleLog.Info($"ROUTE {result.Message}");
        }
        else
        {
            ConsoleLog.Error($"ROUTE {result.Message}");
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            Commands:
              list | refresh       Enumerate GSMTC sessions and media state
              select <number>      Select the target session shown by list
              clear                Clear the selected target
              status               Show selected target and hook state
              play | pause         Send an explicit playback command
              toggle               Toggle play/pause on the selected target
              next | previous      Skip on the selected target
              stop                 Stop the selected target
              hook on | hook off   Enable/disable physical media-key interception
              help                 Show this help
              quit | exit          Disable the hook and exit

            Safety behavior:
              A physical media key is consumed only while a target is selected and that
              target advertises the corresponding control. Otherwise it passes through.
            """);
    }
}
