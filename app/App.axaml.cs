using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace LuminaIDE;

public class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // A bug in one handler shouldn't take the whole editor (and unsaved work) down with it.
            Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                CrashLog.Write(e.Exception, fatal: false);
                e.Handled = true;
            };

            var settings = Settings.Load();
            var ext = new ExtensionHost();
            ext.Load();

            // luminaide [folder] [file...] [--theme "Name"] [--view explorer|search|extensions] [--terminal] [--agent] [--ask "prompt"] [--run] [--screenshot out.png [--wait ms]]
            string? folder = null, themeName = null, view = null, shot = null, ask = null, page = null;
            bool terminal = false, agent = false, run = false;
            int wait = 2500;
            var files = new List<string>();
            var args = desktop.Args ?? [];
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--theme" && i + 1 < args.Length) { themeName = args[++i]; continue; }
                if (args[i] == "--view" && i + 1 < args.Length) { view = args[++i]; continue; }
                if (args[i] == "--terminal") { terminal = true; continue; }
                if (args[i] == "--agent") { agent = true; continue; }
                if (args[i] == "--run") { run = true; continue; }
                if (args[i] == "--page" && i + 1 < args.Length) { page = args[++i]; continue; }
                if (args[i] == "--ask" && i + 1 < args.Length) { ask = args[++i]; agent = true; continue; }
                if (args[i] == "--wait" && i + 1 < args.Length && int.TryParse(args[++i], out var ms)) { wait = ms; continue; }
                if (args[i] == "--screenshot" && i + 1 < args.Length) { shot = Path.GetFullPath(args[++i]); continue; }
                var full = Path.GetFullPath(args[i]);
                if (Directory.Exists(full)) folder = full;
                else if (File.Exists(full)) files.Add(full);
            }
            if (folder is null && files.Count > 0) folder = Path.GetDirectoryName(files[0]);

            var theme = ext.Themes.FirstOrDefault(t => t.Name == (themeName ?? settings.Theme)) ?? ext.Themes[0];
            ThemeManager.Apply(theme);

            var window = new MainWindow(settings, ext, folder, files);
            window.ApplyStartup(view, terminal, agent, ask, run, page);
            if (shot is not null) window.CaptureAndExit(shot, wait);
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
