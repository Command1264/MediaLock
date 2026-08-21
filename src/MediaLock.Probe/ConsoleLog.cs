namespace MediaLock.Probe;

internal static class ConsoleLog
{
    private static readonly Lock Sync = new();

    public static void Info(string message)
    {
        lock (Sync)
        {
            Console.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss.fff}] {message}");
        }
    }

    public static void Error(string message)
    {
        lock (Sync)
        {
            var previousColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss.fff}] ERROR: {message}");
            Console.ForegroundColor = previousColor;
        }
    }
}
