using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace LuminaIDE.Builder;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => AppBuilder.Configure<BuilderApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
}

public sealed class BuilderApp : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new BuilderWindow();
        base.OnFrameworkInitializationCompleted();
    }
}

public sealed class BuilderWindow : Window
{
    readonly TextBox _source = new() { Watermark = "Choose the extracted LuminaIDE source folder" };
    readonly TextBox _log = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
        FontFamily = new FontFamily("Cascadia Code, Menlo, DejaVu Sans Mono"), FontSize = 12, MinHeight = 200 };
    readonly TextBlock _status = new() { Text = "Ready · Start by checking your tools", TextWrapping = TextWrapping.Wrap };
    readonly ProgressBar _progress = new() { Height = 3, Minimum = 0, Maximum = 100 };
    readonly Button _cancel = new() { Content = "Stop", IsEnabled = false };
    readonly List<Button> _actions = [];
    Process? _process;
    bool _busy, _cancelled;
    string _lastFolder = "";

    public BuilderWindow()
    {
        Title = "LuminaIDE · Build studio";
        Width = 960; Height = 740; MinWidth = 700; MinHeight = 560;
        Background = Brush.Parse("#10131D");
        var content = new Grid { RowDefinitions = new("Auto,Auto,Auto,Auto,*,Auto"), Margin = new Thickness(30) };
        var heading = new StackPanel { Spacing = 6 };
        heading.Children.Add(new TextBlock { Text = "LUMINAIDE  /  DEVELOPER TOOLS", FontSize = 11, Foreground = Brush.Parse("#A99AFF"), LetterSpacing = 2 });
        heading.Children.Add(new TextBlock { Text = "Build something yours.", FontSize = 30, FontWeight = FontWeight.Bold });
        heading.Children.Add(new TextBlock { Text = "Check your tools, build the editor, or create a download for this computer.", Foreground = Brush.Parse("#A7B0C3"), TextWrapping = TextWrapping.Wrap });
        content.Children.Add(heading);
        var sourceRow = new Grid { ColumnDefinitions = new("*,Auto") };
        sourceRow.Children.Add(_source);
        var browse = new Button { Content = "Choose folder…", Margin = new Thickness(10, 0, 0, 0) }; Grid.SetColumn(browse, 1); sourceRow.Children.Add(browse);
        _actions.Add(browse);
        browse.Click += async (_, _) => {
            try {
            var folders = await StorageProvider.OpenFolderPickerAsync(new() { Title = "LuminaIDE source folder", AllowMultiple = false });
            if (folders.Count > 0 && folders[0].Path.IsFile) _source.Text = folders[0].Path.LocalPath;
            } catch (Exception ex) { _status.Text = ex.Message; }
        };
        Grid.SetRow(sourceRow, 1); content.Children.Add(sourceRow);
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (label, mode) in new[] { ("1  Check tools", "--check"), ("2  Build editor", "build"), ("Build + test", "test"), ("3  Create download", "package") })
        {
            var button = new Button { Content = label, Margin = new Thickness(0, 0, 10, 8), Padding = new Thickness(16, 12) };
            button.Click += async (_, _) => await Run(mode);
            _actions.Add(button); actions.Children.Add(button);
        }
        Grid.SetRow(actions, 2); content.Children.Add(actions);
        var hint = new TextBlock { Text = "Requires .NET 8 SDK, Rust, CMake and a C++ compiler. Packages include .NET.\nBuilds target this OS and architecture; use GitHub Actions for other computers.", Foreground = Brush.Parse("#A7B0C3"), FontSize = 12, TextWrapping = TextWrapping.Wrap };
        Grid.SetRow(hint, 3); content.Children.Add(hint);
        Grid.SetRow(_log, 4); content.Children.Add(_log);
        var footer = new StackPanel { Spacing = 10 };
        footer.Children.Add(_progress); footer.Children.Add(_status);
        var links = new WrapPanel();
        AddLink(links, "Setup guide", () => Open(Path.Combine(Folder(), "docs", "BUILDING.md")));
        AddLink(links, "Open downloads", () => { var dir = Path.Combine(Folder(), "dist"); Directory.CreateDirectory(dir); Open(dir); });
        AddLink(links, "Save log…", async () => {
            try {
            var file = await StorageProvider.SaveFilePickerAsync(new() { SuggestedFileName = "lumina-build.log" });
            if (file is not null) { await using var stream = await file.OpenWriteAsync(); stream.SetLength(0); await using var writer = new StreamWriter(stream); await writer.WriteAsync(_log.Text); }
            } catch (Exception ex) { _status.Text = "Could not save log: " + ex.Message; }
        });
        _cancel.Click += (_, _) => Cancel(); links.Children.Add(_cancel);
        footer.Children.Add(links); Grid.SetRow(footer, 5); content.Children.Add(footer); foreach (var child in content.Children.OfType<Control>().Where(c => Grid.GetRow(c) < 5)) child.Margin = new Thickness(0, 0, 0, 18);
        Content = content;
        _source.Text = FindSource();
        Closing += (_, e) => { if (_busy) { e.Cancel = true; Cancel(); _status.Text = "Stopping the build. Close this window once it finishes."; } };
        // Automated GUI smoke check: render without starting a build.
        var args = Environment.GetCommandLineArgs();
        var shot = Array.IndexOf(args, "--screenshot");
        if (shot >= 0 && shot + 1 < args.Length) Opened += async (_, _) => {
            if (args.Contains("--check-tools")) {
                await Run("--check");
                if (!_status.Text!.StartsWith("Complete")) Environment.ExitCode = 1;
            }
            await Task.Delay(800);
            using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)Bounds.Width, (int)Bounds.Height), new Vector(96, 96));
            bitmap.Render(this); bitmap.Save(args[shot + 1]); Close();
        };
    }

    void AddLink(Panel panel, string label, Action action)
    {
        var button = new Button { Content = label, Margin = new Thickness(0, 0, 10, 0) };
        button.Click += (_, _) => { try { action(); } catch (Exception ex) { _status.Text = ex.Message; } };
        panel.Children.Add(button);
    }
    string Folder() => Path.GetFullPath(_source.Text?.Trim() ?? "");
    static void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    static string FindSource()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var d = new DirectoryInfo(start); d is not null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "app", "LuminaIDE.csproj"))) return d.FullName;
        return "";
    }
    void Cancel()
    {
        _cancelled = true;
        try { if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (Exception ex) { Append("Could not stop process: " + ex.Message); }
    }
    void Append(string text) => Dispatcher.UIThread.Post(() => {
        // Bound memory usage during long compiler runs. Save log exports what is displayed.
        var value = (_log.Text ?? "") + text + Environment.NewLine;
        _log.Text = value.Length > 250_000 ? value[^200_000..] : value;
        _log.CaretIndex = _log.Text.Length;
    });
    async Task Run(string mode)
    {
        if (_busy) return;
        try {
            _lastFolder = Folder();
            var script = Path.Combine(_lastFolder, OperatingSystem.IsWindows() ? "build.ps1" : "build.sh");
            if (!File.Exists(script) || !File.Exists(Path.Combine(_lastFolder, "app", "LuminaIDE.csproj")))
                throw new InvalidOperationException("Choose the extracted LuminaIDE source folder first.");
            _busy = true; _cancelled = false;
            _actions.ForEach(b => b.IsEnabled = false); _source.IsEnabled = false; _cancel.IsEnabled = true;
            _progress.IsIndeterminate = true; _log.Text = ""; _status.Text = "Working · " + mode;
            var info = new ProcessStartInfo(OperatingSystem.IsWindows() ? "powershell.exe" : "bash") {
                WorkingDirectory = _lastFolder, UseShellExecute = false, RedirectStandardOutput = true,
                RedirectStandardError = true, CreateNoWindow = true
            };
            if (OperatingSystem.IsWindows()) foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File" }) info.ArgumentList.Add(arg);
            info.ArgumentList.Add(script); info.ArgumentList.Add(mode); info.Environment["NO_COLOR"] = "1";
            _process = new Process { StartInfo = info };
            _process.Start();
            async Task Pump(StreamReader reader) { while (await reader.ReadLineAsync() is { } line) Append(line); }
            await Task.WhenAll(Pump(_process.StandardOutput), Pump(_process.StandardError), _process.WaitForExitAsync());
            _status.Text = _cancelled ? "Stopped · You can restart the build." : _process.ExitCode == 0
                ? mode == "package" ? "Download ready · Open downloads to find the archive and SHA-256 checksum." : "Complete · " + mode
                : "Failed · Read the log above, or open the setup guide.";
            _progress.Value = _process.ExitCode == 0 && !_cancelled ? 100 : 0;
        } catch (Exception ex) { Append(ex.Message); _status.Text = "Could not run · " + ex.Message; }
        finally {
            _process?.Dispose(); _process = null; _busy = false;
            _progress.IsIndeterminate = false; _cancel.IsEnabled = false; _source.IsEnabled = true;
            _actions.ForEach(b => b.IsEnabled = true);
        }
    }
}
