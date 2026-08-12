namespace DiskSpaceTree.Diagnostics;

public static class Logger
{
    private static readonly string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scan.log");
    private static readonly object Lock = new();

    public static void Log(string message)
    {
        try
        {
            var line = $"{DateTime.Now:HH:mm:ss.fff} [{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}";
            lock (Lock)
            {
                File.AppendAllText(LogPath, line);
            }
        }
        catch
        {
            // Logging failures must not break the scan.
        }
    }
}
