namespace LuminaIDE;

/// <summary>
/// Writes unexpected exceptions to ~/.config/luminaide/crash.log so the full message and stack
/// survive even when the process aborts and the terminal output scrolls away.
/// </summary>
static class CrashLog
{
    public static string FilePath => Path.Combine(Settings.ConfigDir, "crash.log");

    /// <summary>Raised for exceptions the app survived, so the window can tell the user.</summary>
    public static event Action<Exception>? Survived;

    public static void Write(Exception? ex, bool fatal)
    {
        var text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {(fatal ? "FATAL" : "handled")}\n{ex}\n\n";
        try
        {
            Directory.CreateDirectory(Settings.ConfigDir);
            File.AppendAllText(FilePath, text);
        }
        catch { /* logging must never make things worse */ }
        Console.Error.Write(text);
        if (!fatal && ex is not null) Survived?.Invoke(ex);
    }
}
