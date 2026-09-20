using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Search;

namespace LuminaIDE;

/// <summary>Explorer tree node. Folders load their children the first time they are expanded.</summary>
public sealed class Node : INotifyPropertyChanged
{
    public string Name { get; }
    public string Path { get; }
    public bool IsDir { get; }
    public ObservableCollection<Node> Children { get; } = [];

    bool _expanded, _loaded;
    readonly HashSet<string>? _reexpand;

    public Node(Entry e, HashSet<string>? reexpand = null) : this(e.name, e.path, e.is_dir, reexpand) { }

    Node(string name, string path, bool isDir, HashSet<string>? reexpand)
    {
        Name = name; Path = path; IsDir = isDir; _reexpand = reexpand;
        if (!isDir) return;
        Children.Add(new Node("…", "", false, null)); // placeholder so the expander shows
        if (reexpand?.Contains(path) == true) IsExpanded = true;
    }

    /// <summary>Paths of every folder currently expanded under <paramref name="nodes"/>.</summary>
    public static HashSet<string> ExpandedPaths(IEnumerable<Node> nodes)
    {
        var set = new HashSet<string>();
        void Walk(IEnumerable<Node> level)
        {
            foreach (var n in level.Where(n => n.IsDir && n._expanded)) { set.Add(n.Path); Walk(n.Children); }
        }
        Walk(nodes);
        return set;
    }

    public bool IsExpanded
    {
        get => _expanded;
        set
        {
            if (_expanded == value) return;
            _expanded = value;
            if (value && !_loaded)
            {
                _loaded = true;
                Children.Clear();
                foreach (var e in Core.ListDir(Path)) Children.Add(new Node(e, _reexpand));
            }
            PropertyChanged?.Invoke(this, new(nameof(IsExpanded)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

sealed class Doc
{
    public required string Path { get; init; }
    public required TextEditor Editor { get; init; }
    public required SyntaxColorizer Colorizer { get; init; }
    public required Border Tab { get; init; }
    /// <summary>What goes into the editor area: the editor itself, or editor + preview for Markdown.</summary>
    public required Control Host { get; init; }
    public ScrollViewer? Preview { get; init; }
    /// <summary>Row above the editor for informational banners (currently: what a license file says).</summary>
    public required ContentControl BannerSlot { get; init; }
    public bool IsLicense { get; set; }
    public bool BannerDismissed { get; set; }
    public bool BannerCollapsed { get; set; }
    public Grid? Split { get; init; }
    public bool IsMarkdown => Preview is not null;
    public required TextBlock DirtyDot { get; init; }
    public string? LanguageId { get; set; }
    public bool Dirty { get; private set; }
    public void SetDirty(bool dirty) { Dirty = dirty; Tab.Classes.Set("dirty", dirty); }
    public bool CloseArmed { get; set; }
    /// <summary>True while we replace the buffer ourselves, so it doesn't count as a user edit.</summary>
    public bool Loading { get; set; }
    /// <summary>Last write time of the file as we last read or wrote it.</summary>
    public DateTime Stamp { get; set; }
    public string Title => System.IO.Path.GetFileName(Path);
}

sealed record ResultItem(Hit Hit, string Text, string Where);

public partial class MainWindow : Window
{
    enum SideView { Explorer, Search, Web, Extensions }

    static readonly Regex Ansi = new(@"\x1B\[[0-9;?]*[ -/]*[@-~]|\x1B\][^\x07\x1B]*(\x07|\x1B\\)", RegexOptions.Compiled);
    const int TermMaxChars = 200_000;
    const int MaxHighlightChars = 1_500_000;
    const long MaxOpenBytes = 8 * 1024 * 1024;
    const int MaxLineChars = 20_000;
    const string EditorFont = "avares://LuminaIDE/Assets/Fonts#JetBrains Mono,Cascadia Code,Fira Code,DejaVu Sans Mono,Noto Sans Mono,monospace";

    readonly Settings _settings;
    readonly ExtensionHost _ext;
    readonly List<Doc> _docs = [];
    readonly HashSet<Doc> _pendingHighlight = [];
    readonly DispatcherTimer _highlightTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    readonly HashSet<Doc> _pendingPreview = [];
    readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    readonly DispatcherTimer _termTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    readonly StringBuilder _termText = new();
    Terminal? _term;
    Doc? _active;
    string? _root;
    SideView _view = SideView.Explorer;
    bool _sideVisible = true;
    bool _agentVisible;

    public MainWindow(Settings settings, ExtensionHost ext, string? folder, IReadOnlyList<string> files)
    {
        _settings = settings;
        _ext = ext;
        InitializeComponent();

        // Agent panel
        Agent.Init(_settings);
        Agent.RootProvider = () => _root;
        Agent.ContextProvider = GetEditorContext;
        Agent.CloseRequested += () => SetAgentVisible(false);
        Agent.TurnFinished += OnAgentFinished;
        ActAgent.Tapped += (_, _) => SetAgentVisible(!_agentVisible);
        Welcome.AgentRequested += () => SetAgentVisible(true);
        Activated += (_, _) => RefreshFromDisk();
        Deactivated += (_, _) => SaveDirtyOnFocusLost();
        _autoSaveTimer.Tick += (_, _) => RunAutoSave();
        InitWeb();
        ActSettings.Tapped += (_, _) => OpenSettings();
        ActWeb.Tapped += (_, _) => ShowView(SideView.Web, toggle: true);
        PageClose.Tapped += (_, _) => HidePage();
        Quick.Closed += _ => { if (_active is not null) _active.Editor.Focus(); };

        // Sidebar & navigation
        ActExplorer.Tapped += (_, _) => ShowView(SideView.Explorer, toggle: true);
        ActSearch.Tapped += (_, _) => ShowView(SideView.Search, toggle: true);
        ActExtensions.Tapped += (_, _) => ShowView(SideView.Extensions, toggle: true);
        OpenFolderButton.Tapped += async (_, _) => await PickFolder();
        NoFolderOpen.Tapped += async (_, _) => await PickFolder();
        RefreshButton.Tapped += (_, _) => LoadTree();
        NewFileButton.Tapped += (_, _) => BeginNewFile();
        NewFileBox.KeyDown += OnNewFileKey;
        Tree.DoubleTapped += (_, _) => { if (Tree.SelectedItem is Node { IsDir: false } n) OpenFile(n.Path); };
        Results.DoubleTapped += (_, _) => { if (Results.SelectedItem is ResultItem r) OpenFile(r.Hit.path, r.Hit.line); };
        SearchBox.KeyDown += OnSearchKey;
        ExtOpenFolder.Tapped += (_, _) => OpenInFileManager(ExtensionHost.UserDir);
        ExtReload.Tapped += (_, _) => ReloadExtensions();

        // Status bar pickers
        StatusLang.Tapped += (_, _) => ShowLanguageMenu();
        StatusTheme.Tapped += (_, _) => ShowThemeMenu();

        // Terminal
        TermInput.KeyDown += OnTermKey;
        TermClose.Tapped += (_, _) => SetTerminalVisible(false);
        _termTimer.Tick += (_, _) => PumpTerminal();

        // Welcome
        Welcome.OpenFolderRequested += async () => await PickFolder();
        Welcome.NewFileRequested += BeginNewFile;
        Welcome.NewWebsiteRequested += () => ShowView(SideView.Web);
        Welcome.SettingsRequested += OpenSettings;
        Welcome.PaletteRequested += ShowPalette;
        Welcome.ReleaseNotesRequested += OpenChangelog;
        Welcome.RecentRequested += f => SetRoot(f);
        Welcome.ThemeRequested += n => SetTheme(n);

        _licenseTimer.Tick += (_, _) =>
        {
            _licenseTimer.Stop();
            foreach (var d in _pendingLicense.ToArray()) ShowLicense(d);
            _pendingLicense.Clear();
        };
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            foreach (var d in _pendingPreview.ToArray()) RenderPreview(d);
            _pendingPreview.Clear();
        };
        RunButton.Tapped += (_, _) => RunActive();
        StatusPreview.Tapped += (_, _) => ShowPreviewMenu();

        _highlightTimer.Tick += (_, _) =>
        {
            _highlightTimer.Stop();
            foreach (var d in _pendingHighlight.ToArray()) Rehighlight(d);
            _pendingHighlight.Clear();
        };

        Opened += (_, _) => Activate(); // come to the front when launched from a terminal or file manager
        AddHandler(KeyDownEvent, OnWindowKey, RoutingStrategies.Tunnel);
        ThemeManager.Changed += OnThemeChanged;
        CrashLog.Survived += OnCrashSurvived;
        Closed += (_, _) =>
        {
            ThemeManager.Changed -= OnThemeChanged;
            CrashLog.Survived -= OnCrashSurvived;
            _termTimer.Stop();
            _term?.Dispose();
            Agent.Shutdown();
        };

        SetRoot(folder, addRecent: folder is not null);
        RefreshExtensionsView();
        ApplySettings();
        UpdateChrome();
        foreach (var f in files) OpenFile(f);

        if (Settings.LoadProblem is { } problem) Flash(problem, 12);
        // After an update, show what changed (but not on a brand-new install, and not when opened for a specific file).
        bool updated = _settings.LastVersion.Length > 0 && _settings.LastVersion != AppInfo.Version;
        if (_settings.LastVersion != AppInfo.Version) { _settings.LastVersion = AppInfo.Version; _settings.Save(); }
        if (updated && files.Count == 0) Opened += (_, _) => OpenChangelog();
    }

    public void ApplyStartup(string? view, bool terminal, bool agent = false, string? ask = null, bool run = false, string? page = null)
    {
        switch (page)
        {
            case "settings": OpenSettings(); break;
            case "changelog": OpenChangelog(); break;
            case "palette": Opened += (_, _) => ShowPalette(); break;
            case "quickopen": Opened += (_, _) => ShowQuickOpen(); break;
            case { } p when p.StartsWith("license:"):
                Opened += (_, _) => { if (_ext.Licenses.FirstOrDefault(l => l.Id.Equals(p[8..], StringComparison.OrdinalIgnoreCase)) is { } l) ShowPage(l.Name, LicenseCards.Page(l, Platform.Open)); };
                break;
        }
        if (run) Opened += (_, _) => RunActive();
        if (agent) SetAgentVisible(true);
        if (ask is not null) Opened += (_, _) => Agent.Ask(ask);
        if (Enum.TryParse<SideView>(view, ignoreCase: true, out var v)) ShowView(v);
        if (terminal) SetTerminalVisible(true);
    }

    /// <summary>Renders the window to a PNG and quits; used to check the UI without a desktop.</summary>
    public void CaptureAndExit(string path, int waitMs = 2500)
    {
        Opened += async (_, _) =>
        {
            await Task.Delay(waitMs);
            var size = new PixelSize((int)Bounds.Width, (int)Bounds.Height);
            var bmp = new Avalonia.Media.Imaging.RenderTargetBitmap(size, new Vector(96, 96));
            bmp.Render(this);
            bmp.Save(path);
            Close();
        };
    }

    // ---------------------------------------------------------------- chrome --

    void UpdateChrome()
    {
        var hasDocs = _docs.Count > 0;
        TabBar.IsVisible = hasDocs;
        Welcome.IsVisible = !hasDocs;
        EditorHost.IsVisible = hasDocs;
        if (!hasDocs) Welcome.Refresh(_settings, _ext);

        StatusThemeText.Text = ThemeManager.Current.Name;
        StatusLangText.Text = _active is null ? "" : _ext.LanguageName(_active.LanguageId);
        StatusLang.IsVisible = _active is not null;
        var lang = _ext.Languages.FirstOrDefault(l => l.Id == _active?.LanguageId);
        RunButton.IsVisible = _active is not null && !string.IsNullOrEmpty(lang?.Run);
        StatusPreview.IsVisible = _active?.IsMarkdown == true;
        StatusPreviewText.Text = _settings.MarkdownMode switch { "preview" => "Preview only", "editor" => "Preview: off", _ => "Preview: side by side" };
        StatusPos.IsVisible = _active is not null;
        if (_active is null) StatusPos.Text = "";
        StatusLeft.Text = _root is null ? "No folder open" : Path.GetFileName(_root.TrimEnd('/', '\\'));
        Title = _active is null ? "LuminaIDE" : $"{_active.Title} — LuminaIDE";
        Agent.UpdateContext(_active is null ? null : RelativeToRoot(_active.Path));
    }

    void Flash(string message, int seconds = 3)
    {
        StatusLeft.Text = message;
        DispatcherTimer.RunOnce(() => { if (StatusLeft.Text == message) UpdateChrome(); }, TimeSpan.FromSeconds(seconds));
    }

    void OnCrashSurvived(Exception ex) =>
        Flash($"Something went wrong ({ex.GetType().Name}: {ex.Message}) — details in {CrashLog.FilePath.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "~")}", 10);

    void ShowView(SideView view, bool toggle = false)
    {
        if (toggle && _view == view && _sideVisible) { SetSidebarVisible(false); return; }
        SetSidebarVisible(true);
        _view = view;
        ExplorerView.IsVisible = view == SideView.Explorer;
        SearchView.IsVisible = view == SideView.Search;
        ExtensionsView.IsVisible = view == SideView.Extensions;
        Web.IsVisible = view == SideView.Web;
        ActExplorer.Classes.Set("active", view == SideView.Explorer);
        ActSearch.Classes.Set("active", view == SideView.Search);
        ActExtensions.Classes.Set("active", view == SideView.Extensions);
        ActWeb.Classes.Set("active", view == SideView.Web);
        if (view == SideView.Web) Web.Refresh();
        if (view == SideView.Search) { SearchBox.Focus(); SearchBox.SelectAll(); }
    }

    void SetSidebarVisible(bool visible)
    {
        _sideVisible = visible;
        Body.ColumnDefinitions[1].Width = new GridLength(visible ? 280 : 0);
        SideSplitter.IsVisible = visible;
    }

    void SetTerminalVisible(bool visible)
    {
        var rows = MainGrid.RowDefinitions;
        rows[2].Height = new GridLength(visible ? 4 : 0);
        rows[3].Height = new GridLength(visible ? 260 : 0);
        TermSplitter.IsVisible = visible;
        if (visible)
        {
            if (_term is null) StartTerminal();
            TermInput.Focus();
        }
        else _active?.Editor.Focus();
    }

    bool TerminalVisible => MainGrid.RowDefinitions[3].Height.Value > 0;

    // ------------------------------------------------------ markdown preview --

    static bool IsMarkdownPath(string path) =>
        Path.GetExtension(path).Equals(".md", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(path).Equals(".markdown", StringComparison.OrdinalIgnoreCase);

    /// <summary>Shows or hides the editor and preview halves according to the current mode.</summary>
    void ApplyMarkdownMode(Doc d)
    {
        if (d.Split is null || d.Preview is null) return;
        var mode = _settings.MarkdownMode;
        var star = new GridLength(1, GridUnitType.Star);
        var cols = d.Split.ColumnDefinitions;
        cols[0].Width = mode == "preview" ? new GridLength(0) : star;
        cols[1].Width = new GridLength(mode == "split" ? 4 : 0);
        cols[2].Width = mode == "editor" ? new GridLength(0) : star;
        d.Editor.IsVisible = mode != "preview";
        d.Preview.IsVisible = mode != "editor";
        if (mode != "editor") RenderPreview(d);
    }

    void SchedulePreview(Doc d)
    {
        if (_settings.MarkdownMode == "editor") return;
        _pendingPreview.Add(d);
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    void RenderPreview(Doc d)
    {
        if (d.Preview is null || _settings.MarkdownMode == "editor") return;
        Control content;
        try
        {
            content = d.Editor.Document.TextLength > 600_000
                ? new TextBlock { Text = "This file is too large to preview.", Margin = new Thickness(32), Opacity = 0.7 }
                : new MarkdownRenderer(Path.GetDirectoryName(d.Path), _ext, p => OpenFile(p), m => Flash(m), _root, _settings.MarkdownRemoteImages, _settings.MarkdownAnimateGifs).Render(d.Editor.Text);
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, fatal: false);
            content = new TextBlock { Text = "The preview failed for this document.", Margin = new Thickness(32), Opacity = 0.7 };
        }
        var offset = d.Preview.Offset; // keep the reading position while typing
        d.Preview.Content = content;
        Dispatcher.UIThread.Post(() => d.Preview.Offset = offset, DispatcherPriority.Background);
    }

    void SetMarkdownMode(string mode)
    {
        _settings.MarkdownMode = mode;
        _settings.Save();
        foreach (var d in _docs.Where(d => d.IsMarkdown)) ApplyMarkdownMode(d);
        UpdateChrome();
    }

    void CyclePreview() => SetMarkdownMode(_settings.MarkdownMode switch { "editor" => "split", "split" => "preview", _ => "editor" });

    void ShowPreviewMenu() => ShowMenu(StatusPreview, new (string, bool, Action)[]
    {
        ("Side by side", _settings.MarkdownMode == "split", () => SetMarkdownMode("split")),
        ("Preview only", _settings.MarkdownMode == "preview", () => SetMarkdownMode("preview")),
        ("Editor only", _settings.MarkdownMode == "editor", () => SetMarkdownMode("editor")),
    });

    // ------------------------------------------------------------------ run --

    static string ShellQuote(string s) => Platform.Quote(s);

    /// <summary>Runs the active file in the terminal with its language's run command (F5).</summary>
    void RunActive()
    {
        if (_active is not { } d) { Flash("Open a file to run."); return; }
        var lang = _ext.Languages.FirstOrDefault(l => l.Id == d.LanguageId);
        if (string.IsNullOrEmpty(lang?.Run))
        {
            Flash($"No run command for {lang?.Name ?? "this kind of file"} — add a \"run\" line to its language file (see docs/LANGUAGES.md)", 6);
            return;
        }
        if (d.Dirty) Save();

        var dir = Path.GetDirectoryName(d.Path)!;
        var command = lang.Run
            .Replace("{file}", ShellQuote(d.Path))
            .Replace("{dir}", ShellQuote(dir))
            .Replace("{name}", ShellQuote(Path.GetFileNameWithoutExtension(d.Path)));

        RunInTerminal(dir, command);
    }

    // -------------------------------------------------------------- agent --

    void SetAgentVisible(bool visible)
    {
        _agentVisible = visible;
        Body.ColumnDefinitions[5].MinWidth = visible ? 320 : 0;
        Body.ColumnDefinitions[5].Width = new GridLength(visible ? 420 : 0);
        AgentSplitter.IsVisible = visible;
        ActAgent.Classes.Set("active", visible);
        if (visible) Agent.FocusInput(); else _active?.Editor.Focus();
    }

    string RelativeToRoot(string path) => _root is null ? Path.GetFileName(path) : Path.GetRelativePath(_root, path);

    (string? File, string? Selection) GetEditorContext() =>
        _active is null ? (null, null) : (RelativeToRoot(_active.Path), _active.Editor.SelectedText);

    /// <summary>The agent may have created or edited files: refresh the tree and any open tabs.</summary>
    void OnAgentFinished()
    {
        LoadTree();
        RefreshFromDisk();
    }

    /// <summary>
    /// Reloads open files that changed on disk (by an agent, the terminal, git...). Tabs with
    /// unsaved edits are left alone, with a warning, so we never discard the user's typing.
    /// </summary>
    void RefreshFromDisk()
    {
        foreach (var d in _docs.ToArray())
        {
            if (!File.Exists(d.Path)) continue;
            var stamp = File.GetLastWriteTimeUtc(d.Path);
            if (stamp == d.Stamp) continue;
            d.Stamp = stamp;

            if (d.Dirty) { Flash($"{d.Title} changed on disk — your unsaved edits are kept; saving will overwrite the disk version"); continue; }

            var text = ReadForEditor(d.Path);
            if (text is null || text == d.Editor.Text) continue;
            var caret = d.Editor.CaretOffset;
            d.Loading = true;
            d.Editor.Text = text;
            d.Loading = false;
            d.Editor.CaretOffset = Math.Min(caret, text.Length);
            Rehighlight(d);
            if (d.IsMarkdown) RenderPreview(d);
            Flash($"Reloaded {d.Title} (changed on disk)");
        }
    }

    // ------------------------------------------------------------- themes --

    void OnThemeChanged()
    {
        foreach (var d in _docs) { StyleEditor(d); RenderPreview(d); }
        ApplyWindowEffect();
        StatusThemeText.Text = ThemeManager.Current.Name;
        if (_docs.Count == 0) Welcome.Refresh(_settings, _ext);
    }

    void SetTheme(string name, bool save = true)
    {
        var theme = _ext.Themes.FirstOrDefault(t => t.Name == name);
        if (theme is null) return;
        ThemeManager.Apply(theme);
        if (!save) return;
        _settings.Theme = name;
        _settings.Save();
    }

    void StyleEditor(Doc d)
    {
        var t = ThemeManager.Current;
        var ed = d.Editor;
        ed.Foreground = new SolidColorBrush(t.UiColor("foreground"));
        ed.LineNumbersForeground = new SolidColorBrush(t.UiColor("lineNumber"));
        var view = ed.TextArea.TextView;
        view.CurrentLineBackground = new SolidColorBrush(t.UiColor("lineHighlight"));
        view.CurrentLineBorder = new Pen(Brushes.Transparent, 0);
        ed.TextArea.SelectionBrush = new SolidColorBrush(t.UiColor("selection"));
        ed.TextArea.SelectionForeground = null;
        ed.TextArea.SelectionBorder = null;
        ed.TextArea.Caret.CaretBrush = new SolidColorBrush(t.UiColor("accent"));
        view.Redraw();
    }

    void ShowThemeMenu() =>
        ShowMenu(StatusTheme, _ext.Themes.Select(t => (t.Name, t.Name == ThemeManager.Current.Name, (Action)(() => SetTheme(t.Name)))));

    void ShowLanguageMenu()
    {
        if (_active is not { } d) return;
        var items = new List<(string, bool, Action)> { ("Plain Text", d.LanguageId is null, () => SetLanguage(d, null)) };
        items.AddRange(_ext.Languages.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .Select(l => (l.Name, l.Id == d.LanguageId, (Action)(() => SetLanguage(d, l.Id)))));
        ShowMenu(StatusLang, items);
    }

    void ShowMenu(Control anchor, IEnumerable<(string Header, bool Checked, Action Act)> items)
    {
        var menu = new ContextMenu { Placement = PlacementMode.TopEdgeAlignedRight };
        foreach (var (header, isChecked, act) in items)
        {
            var mi = new MenuItem { Header = header, Icon = isChecked ? new TextBlock { Text = "✓" } : null };
            mi.Click += (_, _) => act();
            menu.Items.Add(mi);
        }
        menu.Open(anchor);
    }

    // ---------------------------------------------------------- workspace --

    void SetRoot(string? folder, bool addRecent = true)
    {
        var oldRoot = _root;
        _root = folder;
        if (folder != oldRoot) Agent.NewChat(); // a conversation belongs to one folder
        var has = folder is not null;
        NoFolder.IsVisible = !has;
        Tree.IsVisible = has;
        RootLabel.Text = has ? Path.GetFileName(folder!.TrimEnd('/', '\\')).ToUpperInvariant() : "EXPLORER";
        if (folder != oldRoot) Tree.ItemsSource = null; // never carry expansion over from another folder
        LoadTree();
        SearchBox.Text = "";
        Results.ItemsSource = null;
        SearchHint.Text = has ? "Type something and press Enter." : "Open a folder to search it.";

        if (has && addRecent) { _settings.AddRecent(folder!); _settings.Save(); }
        if (_term is not null) StartTerminal();
        UpdateChrome();
    }

    /// <summary>Re-lists the explorer, keeping the folders that were expanded.</summary>
    void LoadTree()
    {
        if (_root is null) { Tree.ItemsSource = null; return; }
        var keep = Tree.ItemsSource is IEnumerable<Node> old ? Node.ExpandedPaths(old) : null;
        Tree.ItemsSource = new ObservableCollection<Node>(Core.ListDir(_root).Select(e => new Node(e, keep)));
    }

    async Task PickFolder()
    {
        var picked = await StorageProvider.OpenFolderPickerAsync(new() { Title = "Open folder", AllowMultiple = false });
        if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } path) SetRoot(path);
    }

    void BeginNewFile()
    {
        if (_root is null) { Flash("Open a folder first, then create a file in it."); return; }
        ShowView(SideView.Explorer);
        NewFileBox.Text = "";
        NewFileBox.IsVisible = true;
        NewFileBox.Focus();
    }

    void OnNewFileKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { NewFileBox.IsVisible = false; e.Handled = true; return; }
        if (e.Key != Key.Enter || _root is null) return;
        e.Handled = true;

        var rel = (NewFileBox.Text ?? "").Trim().TrimStart('/', '\\');
        if (rel.Length == 0) return;
        var full = Path.GetFullPath(Path.Combine(_root, rel));
        if (!full.StartsWith(Path.GetFullPath(_root) + Path.DirectorySeparatorChar)) { Flash("File must be inside the open folder."); return; }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            if (!File.Exists(full)) File.WriteAllText(full, "");
        }
        catch (Exception ex) { Flash($"Could not create file: {ex.Message}"); return; }

        NewFileBox.IsVisible = false;
        LoadTree();
        OpenFile(full);
    }

    static void OpenInFileManager(string dir)
    {
        try { Directory.CreateDirectory(dir); } catch { return; }
        Platform.Open(dir);
    }

    // ---------------------------------------------------------- extensions --

    void ReloadExtensions()
    {
        _ext.Load();
        foreach (var d in _docs) { d.LanguageId = Core.LanguageForPath(d.Path); Rehighlight(d); }
        var current = _ext.Themes.FirstOrDefault(t => t.Name == _settings.Theme) ?? _ext.Themes[0];
        ThemeManager.Apply(current);
        RefreshExtensionsView();
        foreach (var d in _docs.Where(d => d.IsLicense)) ShowLicense(d);
        UpdateChrome();
        Flash($"Reloaded {_ext.Extensions.Count} extensions");
    }

    void RefreshExtensionsView()
    {
        ExtList.Children.Clear();
        foreach (var x in _ext.Extensions)
        {
            var title = new StackPanel { Spacing = 4 };
            title.Children.Add(new TextBlock { Text = x.Title, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            var meta = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            title.Children.Add(meta);
            var version = new TextBlock { Text = "v" + x.Manifest.Version, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            version.Classes.Add("muted");
            meta.Children.Add(version);

            var tagText = new TextBlock { Text = x.BuiltIn ? "built-in" : "user", FontSize = 10 };
            tagText.Classes.Add("muted");
            var tag = new Border { Child = tagText, VerticalAlignment = VerticalAlignment.Center };
            tag.Classes.Add("tag");
            meta.Children.Add(tag);

            var desc = new TextBlock { Text = x.Manifest.Description, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
            desc.Classes.Add("muted");
            var parts = new List<string>();
            if (x.LanguageCount > 0) parts.Add($"{x.LanguageCount} language{(x.LanguageCount == 1 ? "" : "s")}");
            if (x.ThemeCount > 0) parts.Add($"{x.ThemeCount} theme{(x.ThemeCount == 1 ? "" : "s")}");
            if (x.LicenseCount > 0) parts.Add($"{x.LicenseCount} licenses");
            var contributes = new TextBlock { Text = string.Join(" · ", parts), FontSize = 11 };
            contributes.Classes.Add("accent");

            var card = new Border { Child = new StackPanel { Spacing = 5, Children = { title, desc, contributes } } };
            card.Classes.Add("card");
            ExtList.Children.Add(card);
        }

        foreach (var problem in _ext.Problems)
        {
            var p = new TextBlock { Text = problem, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new(4, 0) };
            p.Classes.Add("error");
            ExtList.Children.Add(p);
        }

        var hint = new TextBlock
        {
            Text = "Add an extension by dropping a folder with an extension.json into the extensions folder, then press Reload. " +
                   "See docs/LANGUAGES.md and docs/THEMES.md for the formats.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new(4, 6),
        };
        hint.Classes.Add("muted");
        ExtList.Children.Add(hint);
    }

    // ------------------------------------------------------------- editor --

    void OpenFile(string path, int line = 0)
    {
        var doc = _docs.FirstOrDefault(d => d.Path == path);
        if (doc is null)
        {
            var text = ReadForEditor(path);
            if (text is null) return;
            doc = CreateDoc(path, text);
        }

        Activate(doc);
        if (line > 0 && line <= doc.Editor.Document.LineCount)
        {
            doc.Editor.CaretOffset = doc.Editor.Document.GetLineByNumber(line).Offset;
            doc.Editor.ScrollToLine(line);
        }
    }

    /// <summary>
    /// Reads a file for the editor, or says in the status bar why it won't be opened. Huge files,
    /// binaries and single-line megabyte blobs make the text layout freeze and eat gigabytes.
    /// </summary>
    string? ReadForEditor(string path)
    {
        var name = Path.GetFileName(path);
        try
        {
            var size = new FileInfo(path).Length;
            if (size > MaxOpenBytes)
            {
                Flash($"{name} is {size / (1024 * 1024)} MB — too large to open in the editor (limit {MaxOpenBytes / (1024 * 1024)} MB)", 6);
                return null;
            }
            using var fs = File.OpenRead(path);
            var head = new byte[(int)Math.Min(8192, size)];
            int n = fs.Read(head, 0, head.Length);
            if (Array.IndexOf(head, (byte)0, 0, n) >= 0)
            {
                Flash($"{name} looks like a binary file — not opened", 6);
                return null;
            }
        }
        catch (Exception e) { Flash($"Could not read {name}: {e.Message}", 6); return null; }

        var text = Core.ReadFile(path);
        if (text is null) { Flash($"Could not read {name}", 6); return null; }
        if (text.Length > 200_000 && LongestLine(text) > MaxLineChars)
        {
            Flash($"{name} has extremely long lines — not opened (the editor would freeze)", 6);
            return null;
        }
        return text;
    }

    static int LongestLine(string s)
    {
        int best = 0, cur = 0;
        foreach (var c in s)
        {
            if (c == '\n') { if (cur > best) best = cur; cur = 0; }
            else cur++;
        }
        return Math.Max(best, cur);
    }

    Doc CreateDoc(string path, string text)
    {
        var colorizer = new SyntaxColorizer();
        var editor = new TextEditor
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(6, 8, 6, 8),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        editor.Options.EnableHyperlinks = false;
        editor.Options.EnableEmailHyperlinks = false;
        // A little air between the line numbers and the code.
        editor.TextArea.LeftMargins.Add(new Border { Width = 10 });
        SearchPanel.Install(editor); // Ctrl+F find, F3 / Shift+F3 next / previous
        editor.TextArea.TextView.LineTransformers.Add(colorizer);
        editor.Document.Changed += (_, e) => colorizer.Adjust(e.Offset, e.RemovalLength, e.InsertionLength);
        editor.Text = text;

        var icon = new FileIcon { FileName = Path.GetFileName(path), Size = 16 };
        var title = new TextBlock { Text = Path.GetFileName(path), VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
        title.Classes.Add("title");
        var dot = new TextBlock { Text = "●", FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        dot.Classes.Add("accent");
        dot.Classes.Add("dirtydot");
        var cross = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M3,3 L11,11 M11,3 L3,11"), Width = 9, Height = 9, Stretch = Stretch.Uniform, StrokeThickness = 1.5,
            StrokeLineCap = PenLineCap.Round, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        cross.Classes.Add("cross");
        var close = new Border { Child = cross, Width = 20, Height = 20, CornerRadius = new CornerRadius(5) };
        close.Classes.Add("item");
        close.Classes.Add("closebtn");
        var tab = new Border
        {
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                Children = { icon, title, new Grid { Width = 20, Height = 20, Margin = new(2, 0, 0, 0), Children = { dot, close } } },
            },
        };
        tab.Classes.Add("tab");
        ToolTip.SetTip(tab, path);

        Control host = editor;
        ScrollViewer? preview = null;
        Grid? split = null;
        if (IsMarkdownPath(path))
        {
            preview = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns };
            split = new Grid { ColumnDefinitions = new("*,4,*") };
            Grid.SetColumn(splitter, 1);
            Grid.SetColumn(preview, 2);
            split.Children.Add(editor);
            split.Children.Add(splitter);
            split.Children.Add(preview);
            host = split;
        }

        // Every document lives in a frame with a banner row above it (used for license explanations).
        var bannerSlot = new ContentControl { IsVisible = false };
        var frame = new Grid { RowDefinitions = new("Auto,*") };
        frame.Children.Add(bannerSlot);
        Grid.SetRow(host, 1);
        frame.Children.Add(host);

        var doc = new Doc
        {
            Path = path, Editor = editor, Colorizer = colorizer, Tab = tab, DirtyDot = dot,
            Host = frame, BannerSlot = bannerSlot, IsLicense = LicenseFiles.IsLicenseFile(path), Preview = preview, Split = split,
            LanguageId = Core.LanguageForPath(path),
            Stamp = File.GetLastWriteTimeUtc(path),
        };

        editor.TextChanged += (_, _) =>
        {
            if (doc.Loading) return;
            doc.CloseArmed = false;
            if (!doc.Dirty) doc.SetDirty(true);
            ScheduleHighlight(doc);
            ScheduleAutoSave(doc);
            if (doc.IsMarkdown) SchedulePreview(doc);
            if (doc.IsLicense) ScheduleLicense(doc);
        };
        editor.TextArea.Caret.PositionChanged += (_, _) => { if (ReferenceEquals(_active, doc)) UpdatePosition(); };
        tab.PointerPressed += (_, e) =>
        {
            var props = e.GetCurrentPoint(tab).Properties;
            if (props.IsMiddleButtonPressed) Close(doc);
            else if (props.IsLeftButtonPressed) Activate(doc);
        };
        close.Tapped += (_, e) => { e.Handled = true; Close(doc); };

        ApplyEditorSettings(doc);
        StyleEditor(doc);
        Rehighlight(doc);
        if (doc.IsMarkdown) ApplyMarkdownMode(doc);
        _docs.Add(doc);
        TabStrip.Children.Add(tab);
        if (doc.IsLicense) ShowLicense(doc);
        return doc;
    }

    void Activate(Doc doc)
    {
        HidePage();
        _active = doc;
        EditorHost.Content = doc.Host;
        foreach (var d in _docs) d.Tab.Classes.Set("active", ReferenceEquals(d, doc));
        UpdateChrome();
        UpdatePosition();
        doc.Tab.BringIntoView();
        doc.Editor.Focus();
    }

    void Close(Doc doc)
    {
        if (doc.Dirty && !doc.CloseArmed)
        {
            doc.CloseArmed = true;
            Flash($"{doc.Title} has unsaved changes — Ctrl+S to save, or close again to discard");
            return;
        }

        int index = _docs.IndexOf(doc);
        _docs.Remove(doc);
        _pendingHighlight.Remove(doc);
        TabStrip.Children.Remove(doc.Tab);
        if (ReferenceEquals(_active, doc))
        {
            _active = null;
            EditorHost.Content = null;
            if (_docs.Count > 0) Activate(_docs[Math.Min(index, _docs.Count - 1)]);
        }
        UpdateChrome();
    }

    void UpdatePosition()
    {
        if (_active is not { } d) return;
        var c = d.Editor.TextArea.Caret;
        StatusPos.Text = $"Ln {c.Line}, Col {c.Column}";
    }

    // ------------------------------------------------------- highlighting --

    void SetLanguage(Doc d, string? id)
    {
        d.LanguageId = id;
        Rehighlight(d);
        UpdateChrome();
    }

    void ScheduleHighlight(Doc d)
    {
        _pendingHighlight.Add(d);
        _highlightTimer.Stop();
        _highlightTimer.Start();
    }

    void Rehighlight(Doc d)
    {
        var spans = d.LanguageId is not null && d.Editor.Document.TextLength <= MaxHighlightChars
            ? Core.Tokenize(d.LanguageId, d.Editor.Text)
            : [];
        d.Colorizer.SetSpans(spans);
        d.Editor.TextArea.TextView.Redraw();
    }

    // -------------------------------------------------------------- keys --

    void OnWindowKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _pageOpen && !Quick.IsOpen) { HidePage(); e.Handled = true; return; }
        if (e.Key == Key.F5 && (e.KeyModifiers & ~KeyModifiers.Shift) == 0)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) StopRunning(); // Shift+F5 = Ctrl+C in the terminal
            else RunActive();
            e.Handled = true;
            return;
        }
        // Ctrl+` toggles the terminal everywhere (on macOS, Cmd+` is reserved for switching windows).
        if (e.Key == Key.OemTilde && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { SetTerminalVisible(!TerminalVisible); e.Handled = true; return; }
        if (!e.KeyModifiers.HasFlag(Platform.Mod)) return;
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        switch (e.Key)
        {
            case Key.P when !shift: ShowQuickOpen(); break;
            case Key.P when shift: ShowPalette(); break;
            case Key.OemComma: OpenSettings(); break;
            case Key.W when shift: ShowView(SideView.Web); break;
            case Key.OemPlus or Key.Add: ChangeFontSize(1); break;
            case Key.OemMinus or Key.Subtract: ChangeFontSize(-1); break;
            case Key.D0 when !shift: ChangeFontSize(0); break;
            case Key.S when !shift: Save(); break;
            case Key.W when _active is not null: Close(_active); break;
            case Key.O when !shift: _ = PickFolder(); break;
            case Key.N when !shift: BeginNewFile(); break;
            case Key.B: SetSidebarVisible(!_sideVisible); break;
            case Key.F when shift: ShowView(SideView.Search); break;
            case Key.E when shift: ShowView(SideView.Explorer); break;
            case Key.X when shift: ShowView(SideView.Extensions); break;
            case Key.A when shift: SetAgentVisible(!_agentVisible); break;
            // Leave Ctrl+Shift+V alone inside text boxes (paste); otherwise it cycles the Markdown preview.
            case Key.V when shift && _active?.IsMarkdown == true && TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not TextBox: CyclePreview(); break;
            default: return;
        }
        e.Handled = true;
    }

    // ------------------------------------------------------------ search --

    async void OnSearchKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { SearchBox.Text = ""; Results.ItemsSource = null; return; }
        if (e.Key != Key.Enter || _root is null) return;

        var query = SearchBox.Text?.Trim() ?? "";
        if (query.Length == 0) return;

        var root = _root;
        SearchHint.Text = "Searching…";
        try
        {
            var hits = await Task.Run(() => Core.Search(root, query));
            Results.ItemsSource = hits
                .Select(h => new ResultItem(h, h.text, $"{Path.GetRelativePath(root, h.path)}:{h.line}"))
                .ToList();
            SearchHint.Text = hits.Length == 0 ? $"No results for “{query}”." : "";
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, fatal: false);
            SearchHint.Text = "Search failed.";
        }
    }

    // ---------------------------------------------------------- terminal --

    void StartTerminal()
    {
        _termTimer.Stop();
        _term?.Dispose();
        _termText.Clear();
        TermOut.Text = "";
        SetDevUrl(null);
        var cwd = _root ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _term = Terminal.Open(cwd);
        if (_term is null) { TermOut.Text = "[could not start a shell]"; return; }
        _termTimer.Start();
    }

    void PumpTerminal()
    {
        var chunk = _term?.Poll();
        if (chunk is null)
        {
            _termTimer.Stop();
            Append("\n[process exited]\n");
            return;
        }
        if (chunk.Length > 0) Append(Ansi.Replace(chunk, ""));
    }

    void Append(string text)
    {
        foreach (var c in text)
        {
            if (c == '\r') continue;
            if (c == '\b') { if (_termText.Length > 0 && _termText[^1] != '\n') _termText.Length--; continue; }
            _termText.Append(c);
        }
        if (_termText.Length > TermMaxChars) _termText.Remove(0, _termText.Length - TermMaxChars);
        TermOut.Text = _termText.ToString();
        TermOut.CaretIndex = TermOut.Text.Length; // keeps the view pinned to the bottom
        SniffDevUrl(text);
    }

    void OnTermKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var typed = TermInput.Text ?? "";
            if (Platform.IsWindows) Append($"> {typed}\n"); // the pipe-based Windows shell doesn't echo input itself
            _term?.Send(typed + "\n");
            TermInput.Text = "";
            e.Handled = true;
        }
        else if (e.Key == Key.C && e.KeyModifiers == KeyModifiers.Control && string.IsNullOrEmpty(TermInput.SelectedText))
        {
            _term?.Send("\x03");
            e.Handled = true;
        }
    }
}
