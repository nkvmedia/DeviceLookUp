namespace DeviceLookup.Core.Services;

public static class AppLogger
{
    private static readonly object Gate = new();

    public static string DataFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeviceLookup");

    public static string Folder => Path.Combine(DataFolder, "Logs");
    public static string LogFile => Path.Combine(Folder, $"DeviceLookup-{DateTime.Now:yyyy-MM-dd}.log");

    public static void Info(string message) => Write("INFO", message, null);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                File.AppendAllText(LogFile,
                    $"{DateTime.Now:O} [{level}] {message}{(ex == null ? "" : Environment.NewLine + ex)}{Environment.NewLine}");
            }
        }
        catch { /* logging must never crash the app */ }
    }
}
