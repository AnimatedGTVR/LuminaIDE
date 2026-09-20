using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Text.Json;
using AppTheme = LuminaIDE.Theme;
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
        AvaloniaXamlLoader.Load(this);
        ThemeManager.Apply(AppTheme.Fallback);
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
        FontSize = 12, MinHeight = 140 };
    readonly TextBlock _status = new() { Text = "Ready · Start by checking your tools", TextWrapping = TextWrapping.Wrap };
    readonly ProgressBar _progress = new() { Height = 3, Minimum = 0, Maximum = 100 };
    readonly Button _cancel = new() { Content = "Stop", IsEnabled = false };
    readonly List<Button> _actions = [];
    Process? _process;
    bool _busy, _cancelled;
    string _lastFolder = "";

    public BuilderWindow()
    {
        Title = "LuminaIDE · Build Studio";
        Width = 1060; Height = 820; MinWidth = 800; MinHeight = 720;
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://LuminaIDE.Builder/Assets/icon.png")));
        _log.Classes.Add("log");
        var themes = LoadThemes();
        var preferred = PreferredTheme();
        var initial = themes.FirstOrDefault(t => t.Name == preferred) ?? themes.First(t => t.Name == "Vanta Night");
        ThemeManager.Apply(initial);

        var content = new Grid { RowDefinitions = new("Auto,Auto,Auto,*,Auto"), Margin = new Thickness(32, 28) };
        var header = new Grid { ColumnDefinitions = new("Auto,*,Auto"), Margin = new Thickness(0, 0, 0, 24) };
        var logo = new Image { Source = new Bitmap(AssetLoader.Open(new Uri("avares://LuminaIDE.Builder/Assets/icon.png"))), Width = 64, Height = 64, Margin = new Thickness(0, 0, 18, 0) };
        header.Children.Add(logo);
        var heading = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        heading.Children.Add(Label("LUMINAIDE  /  DEVELOPER TOOLS", "section"));
        heading.Children.Add(new TextBlock { Text = "Build Studio", FontSize = 32, FontWeight = FontWeight.Bold, LetterSpacing = -0.6 });
        heading.Children.Add(Label("From source to something you can share.", "muted"));
        Grid.SetColumn(heading, 1); header.Children.Add(heading);
        var themePanel = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        themePanel.Children.Add(Label("APPEARANCE", "section"));
        var picker = new ComboBox { ItemsSource = themes.Select(t => t.Name).ToArray(), SelectedItem = initial.Name, Width = 178 };
        picker.SelectionChanged += (_, _) => {
            if (picker.SelectedItem is string name && themes.FirstOrDefault(t => t.Name == name) is { } selected)
                ThemeManager.Apply(selected);
        };
        themePanel.Children.Add(picker); Grid.SetColumn(themePanel, 2); header.Children.Add(themePanel);
        content.Children.Add(header);

        var sourcePanel = new StackPanel { Spacing = 10 };
        sourcePanel.Children.Add(Label("SOURCE FOLDER", "section"));
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
        sourcePanel.Children.Add(sourceRow);
        var sourceCard = new Border { Child = sourcePanel, Margin = new Thickness(0, 0, 0, 18) }; sourceCard.Classes.Add("card");
        Grid.SetRow(sourceCard, 1); content.Children.Add(sourceCard);

        var workflow = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 0, 18) };
        workflow.Children.Add(Label("YOUR BUILD WORKFLOW", "section"));
        var actions = new Avalonia.Controls.Primitives.UniformGrid { Columns = 4 };
        foreach (var (number, label, hint, mode) in new[] {
            ("01", "Check tools", "Check the prerequisites", "--check"),
            ("02", "Build editor", "Compile LuminaIDE", "build"),
            ("03", "Build + test", "Check that it works", "test"),
            ("04", "Create download", "Package for this computer", "package") })
        {
            var body = new StackPanel { Spacing = 7 };
            var index = Label(number, "accent"); index.FontSize = 18;
            body.Children.Add(index);
            body.Children.Add(new TextBlock { Text = label, FontWeight = FontWeight.SemiBold, FontSize = 14 });
            var description = Label(hint, "muted"); description.FontSize = 11.5; body.Children.Add(description);
            var button = new Button { Content = body, Margin = new Thickness(0, 0, number == "04" ? 0 : 10, 0) };
            button.Classes.Add("action");
            button.Click += async (_, _) => await Run(mode);
            _actions.Add(button); actions.Children.Add(button);
        }
        workflow.Children.Add(actions);
        var hintText = Label("Requires .NET 8 SDK, Rust, CMake and a C++ compiler. Downloads bundle .NET.", "muted");
        hintText.FontSize = 12; workflow.Children.Add(hintText);
        Grid.SetRow(workflow, 2); content.Children.Add(workflow);

        var output = new Grid { RowDefinitions = new("Auto,*") };
        var outputTitle = Label("BUILD OUTPUT", "section"); outputTitle.Margin = new Thickness(16, 12);
        output.Children.Add(outputTitle); Grid.SetRow(_log, 1); output.Children.Add(_log);
        var outputCard = new Border { Child = output, Padding = new Thickness(0), ClipToBounds = true, Margin = new Thickness(0, 0, 0, 16) };
        outputCard.Classes.Add("card"); Grid.SetRow(outputCard, 3); content.Children.Add(outputCard);

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
        footer.Children.Add(links); Grid.SetRow(footer, 4); content.Children.Add(footer);
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

    static TextBlock Label(string text, string style)
    {
        var label = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        label.Classes.Add(style); return label;
    }
    static List<AppTheme> LoadThemes()
    {
        var themes = new Dictionary<string, AppTheme> { ["Vanta Night"] = AppTheme.Fallback };
        foreach (var folder in new[] { Path.Combine(AppContext.BaseDirectory, "themes"), Settings.ThemesDir })
        {
            try {
                if (Directory.Exists(folder)) foreach (var file in Directory.GetFiles(folder, "*.json"))
                    if (AppTheme.Load(file) is { Name.Length: > 0 } theme) themes[theme.Name] = theme;
            } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return themes.Values.OrderBy(t => t.Name).ToList();
    }
    static string PreferredTheme()
    {
        // Read preferences without rewriting the editor's settings or repairing a broken file.
        try {
            using var json = JsonDocument.Parse(File.ReadAllText(Settings.FilePath), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            foreach (var entry in json.RootElement.EnumerateObject())
                if (entry.Name.Equals("theme", StringComparison.OrdinalIgnoreCase) && entry.Value.ValueKind == JsonValueKind.String)
                    return entry.Value.GetString() ?? "Vanta Night";
        } catch (IOException) { } catch (UnauthorizedAccessException) { } catch (JsonException) { } catch (InvalidOperationException) { }
        return "Vanta Night";
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
