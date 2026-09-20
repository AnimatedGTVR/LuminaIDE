using System.Diagnostics;
using System.Reflection;

namespace LuminaIDE;

/// <summary>Everything that differs between Linux, macOS and Windows, in one place.</summary>
static class Platform
{
    public static bool IsWindows => OperatingSystem.IsWindows();
    public static bool IsMac => OperatingSystem.IsMacOS();
    public static bool IsLinux => OperatingSystem.IsLinux();

    public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Shortens the home folder to "~" for display.</summary>
    public static string Tilde(string path) => Home.Length > 0 && path.StartsWith(Home) ? "~" + path[Home.Length..] : path;

    /// <summary>Opens a URL or a file/folder with the system's default handler. Never uses a shell.</summary>
    public static void Open(string target)
    {
        try
        {
            ProcessStartInfo psi;
            if (IsWindows) psi = new ProcessStartInfo(target) { UseShellExecute = true };
            else
            {
                psi = new ProcessStartInfo(IsMac ? "open" : "xdg-open") { UseShellExecute = false };
                psi.ArgumentList.Add(target);
            }
            Process.Start(psi);
        }
        catch { /* no handler available: nothing sensible to do */ }
    }

    /// <summary>Quotes a value for the terminal's shell (cmd.exe on Windows, POSIX sh otherwise).</summary>
    public static string Quote(string s) =>
        IsWindows ? "\"" + s.Replace("\"", "\\\"") + "\"" : "'" + s.Replace("'", "'\\''") + "'";

    public static string CdCommand(string dir) => IsWindows ? $"cd /d {Quote(dir)}" : $"cd {Quote(dir)}";

    /// <summary>Finds an executable on PATH (honouring PATHEXT on Windows).</summary>
    public static string? Which(string name)
    {
        var exts = IsWindows ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';') : [""];
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            foreach (var ext in exts)
            {
                try
                {
                    var p = Path.Combine(dir, name + ext);
                    if (File.Exists(p)) return p;
                }
                catch { /* malformed PATH entry */ }
            }
        return null;
    }

    /// <summary>The primary shortcut modifier: Cmd on macOS, Ctrl elsewhere.</summary>
    public static Avalonia.Input.KeyModifiers Mod => IsMac ? Avalonia.Input.KeyModifiers.Meta : Avalonia.Input.KeyModifiers.Control;

    /// <summary>Shows a shortcut the way the platform writes it: "Ctrl+Shift+P" becomes "⌘⇧P" on macOS.</summary>
    public static string Keys(string shortcut) =>
        !IsMac || string.IsNullOrEmpty(shortcut) ? shortcut
        : shortcut.Replace("Ctrl+", "⌘").Replace("Shift+", "⇧").Replace("Alt+", "⌥");

    public static string Name => IsWindows ? "Windows" : IsMac ? "macOS" : "Linux";
}

static class AppInfo
{
    public const string Name = "LuminaIDE";

    /// <summary>The version from the project file, without any "+commit" suffix.</summary>
    public static string Version { get; } =
        (Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];
}
