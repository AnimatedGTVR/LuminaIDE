using Avalonia;

namespace LuminaIDE;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Options that need no window are answered before the UI toolkit starts (so they work on headless machines).
        if (args.Any(a => a is "--version" or "-v")) { Cli.AttachConsole(); Console.WriteLine($"{AppInfo.Name} {AppInfo.Version}"); return 0; }
        if (args.Any(a => a is "--help" or "-h" or "/?")) { Cli.AttachConsole(); Console.WriteLine(Cli.UsageText); return 0; }
        if (args.Contains("--selftest")) { Cli.AttachConsole(); return SelfTest.Run(); }

        AppDomain.CurrentDomain.UnhandledException += (_, e) => CrashLog.Write(e.ExceptionObject as Exception, fatal: true);
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
