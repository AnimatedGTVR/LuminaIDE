using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using AppTheme = LuminaIDE.Theme;

namespace LuminaIDE;

/// <summary>Settings, themes and window effects, pages, quick open / command palette, and the web tools.</summary>
public partial class MainWindow
{
    static readonly Regex DevUrlPattern = new(@"https?://(?:localhost|127\.0\.0\.1|0\.0\.0\.0|\[::1\])(?::\d{2,5})?(?:/[^\s'""<>)\]]*)?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    readonly DispatcherTimer _autoSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    readonly HashSet<Doc> _pendingAutoSave = [];
    readonly HashSet<Doc> _pendingLicense = [];
    readonly DispatcherTimer _licenseTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    SettingsView? _settingsView;
    string? _devUrl;
    bool _pageOpen;
    DateTime _resetArmedUntil;

    // ------------------------------------------------------------ settings --

    FontFamily EditorFontFamily()
    {
        var custom = _settings.EditorFontFamily?.Trim();
        return new FontFamily(string.IsNullOrEmpty(custom) ? EditorFont : custom + "," + EditorFont);
    }

    void ApplyEditorSettings(Doc d)
    {
        var ed = d.Editor;
        ed.FontFamily = EditorFontFamily();
        ed.FontSize = _settings.EditorFontSize;
        ed.ShowLineNumbers = _settings.LineNumbers;
        ed.WordWrap = _settings.WordWrap;
        ed.HorizontalScrollBarVisibility = _settings.WordWrap ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        ed.Options.HighlightCurrentLine = _settings.HighlightCurrentLine;
        ed.Options.ConvertTabsToSpaces = _settings.InsertSpaces;
        ed.Options.IndentationSize = _settings.TabSize;
    }

    /// <summary>Pushes the current settings into every open editor, the terminal, the agent panel and the window.</summary>
    void ApplySettings()
    {
        foreach (var d in _docs) { ApplyEditorSettings(d); if (d.IsMarkdown) ApplyMarkdownMode(d); }
        TermOut.FontSize = _settings.TerminalFontSize;
        TermInput.FontSize = _settings.TerminalFontSize;
        Agent.SyncFromSettings();
        ApplyWindowEffect();
        UpdateChrome();
    }

    /// <summary>Keep every theme opaque and independent of compositor effects.</summary>
    void ApplyWindowEffect()
    {
        var opaque = new SolidColorBrush(ThemeManager.Current.UiColor("background"));
        TransparencyLevelHint = [WindowTransparencyLevel.None];
        TransparencyBackgroundFallback = opaque;
        Background = opaque;
    }

    /// <summary>Copies values from another settings object into the live one (other components hold a reference to it).</summary>
    void AdoptSettings(Settings from)
    {
        foreach (var p in typeof(Settings).GetProperties().Where(p => p.CanWrite)) p.SetValue(_settings, p.GetValue(from));
    }

    /// <summary>Called after settings.json is saved from the editor: re-read it and apply.</summary>
    void ReloadSettingsFromDisk()
    {
        var fresh = Settings.Load();
        if (Settings.LoadProblem is { } problem) { Flash(problem, 10); return; }
        var themeChanged = fresh.Theme != _settings.Theme;
        AdoptSettings(fresh);
        if (themeChanged) SetTheme(_settings.Theme, save: false);
        ApplySettings();
        _settingsView?.Rebuild();
        Flash("settings.json applied");
    }

    void ResetSettings()
    {
        if (DateTime.Now > _resetArmedUntil)
        {
            _resetArmedUntil = DateTime.Now.AddSeconds(6);
            Flash("Click “Reset all settings” again within 6 seconds to restore the defaults.", 6);
            return;
        }
        var keepRecent = _settings.RecentFolders.ToList();
        var keepVersion = _settings.LastVersion;
        AdoptSettings(new Settings());
        _settings.RecentFolders = keepRecent;
        _settings.LastVersion = keepVersion;
        _settings.Save();
        SetTheme(_settings.Theme, save: false);
        ApplySettings();
        _settingsView?.Rebuild();
        Flash("Settings were reset to the defaults.");
    }

    void ChangeFontSize(double delta)
    {
        _settings.EditorFontSize = delta == 0 ? 14 : Math.Clamp(_settings.EditorFontSize + delta, 8, 40);
        _settings.Save();
        ApplySettings();
        _settingsView?.Rebuild();
        Flash($"Editor font size {_settings.EditorFontSize:0.#}");
    }

    // ------------------------------------------------------------ save ------

    void Save()
    {
        if (_active is { } d) SaveDoc(d);
    }

    /// <summary>Applies the on-save clean-ups from the settings and returns the text to write.</summary>
    string CleanForSave(string text)
    {
        if (_settings.TrimTrailingWhitespace) text = Regex.Replace(text, @"[ \t]+(?=\r?\n|$)", "");
        if (_settings.InsertFinalNewline && text.Length > 0 && !text.EndsWith('\n'))
            text += text.Contains("\r\n") ? "\r\n" : "\n";
        return text;
    }

    void SaveDoc(Doc d, bool silent = false)
    {
        var text = d.Editor.Text;
        var cleaned = CleanForSave(text);
        if (cleaned != text)
        {
            var caret = d.Editor.CaretOffset;
            d.Loading = true;
            d.Editor.Text = cleaned;
            d.Loading = false;
            d.Editor.CaretOffset = Math.Min(caret, cleaned.Length);
            Rehighlight(d);
            if (d.IsMarkdown) RenderPreview(d);
            text = cleaned;
        }

        if (!Core.WriteFile(d.Path, text)) { Flash($"Could not save {d.Title}"); return; }
        d.SetDirty(false);
        d.CloseArmed = false;
        d.Stamp = File.GetLastWriteTimeUtc(d.Path);
        if (!silent) Flash($"Saved {d.Title}");
        AfterSave(d);
    }

    /// <summary>Saving certain files does more than write them: settings and themes apply live.</summary>
    void AfterSave(Doc d)
    {
        if (string.Equals(d.Path, Settings.FilePath, StringComparison.OrdinalIgnoreCase)) ReloadSettingsFromDisk();
        else if (d.Path.StartsWith(Settings.ThemesDir, StringComparison.OrdinalIgnoreCase) && d.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) ReloadUserTheme(d.Path);
        else if (Path.GetFileName(d.Path) == "package.json") Web.Refresh();
    }

    void ScheduleAutoSave(Doc d)
    {
        if (_settings.AutoSave != "afterDelay") return;
        _pendingAutoSave.Add(d);
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    void RunAutoSave()
    {
        _autoSaveTimer.Stop();
        foreach (var d in _pendingAutoSave.ToArray()) if (d.Dirty && _docs.Contains(d)) SaveDoc(d, silent: true);
        _pendingAutoSave.Clear();
    }

    void SaveDirtyOnFocusLost()
    {
        if (_settings.AutoSave != "onFocusLost") return;
        foreach (var d in _docs.Where(d => d.Dirty).ToArray()) SaveDoc(d, silent: true);
    }

    // -------------------------------------------------------------- themes --

    void ReloadUserTheme(string path)
    {
        var t = AppTheme.Load(path);
        if (t is null || string.IsNullOrWhiteSpace(t.Name)) { Flash("That theme file isn't valid yet — fix it and save again.", 6); return; }
        _ext.Load();
        RefreshExtensionsView();
        SetTheme(t.Name);
        Flash($"Theme “{t.Name}” updated");
    }

    /// <summary>Writes the current theme into the user themes folder under a new name and opens it for editing.</summary>
    void CreateCustomTheme()
    {
        var cur = ThemeManager.Current;
        var names = _ext.Themes.Select(t => t.Name).ToHashSet();
        string name = cur.Name + " Custom";
        for (int i = 2; names.Contains(name); i++) name = $"{cur.Name} Custom {i}";

        var copy = new AppTheme { Name = name, Type = cur.Type, Ui = new(cur.Ui), Syntax = new(cur.Syntax) };
        foreach (var (k, v) in AppTheme.Fallback.Ui) copy.Ui.TryAdd(k, v);        // list every key so nothing is a mystery
        foreach (var (k, v) in AppTheme.Fallback.Syntax) copy.Syntax.TryAdd(k, v);

        try
        {
            Directory.CreateDirectory(Settings.ThemesDir);
            var slug = Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
            var file = Path.Combine(Settings.ThemesDir, slug + ".json");
            File.WriteAllText(file, JsonSerializer.Serialize(copy, new JsonSerializerOptions { WriteIndented = true }) + "\n");
            _ext.Load();
            RefreshExtensionsView();
            SetTheme(name);
            HidePage();
            OpenFile(file);
            Flash("Edit the colors and save — the theme updates as you go.", 8);
        }
        catch (Exception e) { Flash($"Could not create the theme: {e.Message}", 8); }
    }

    // --------------------------------------------------------------- pages --

    void ShowPage(string title, Control content)
    {
        _pageOpen = true;
        PageTitle.Text = title;
        PageContent.Content = content;
        PageLayer.Opacity = 0;
        PageLayer.IsVisible = true;
        Dispatcher.UIThread.Post(() => PageLayer.Opacity = 1, DispatcherPriority.Background); // fades in
    }

    void HidePage()
    {
        if (!_pageOpen) return;
        _pageOpen = false;
        PageLayer.IsVisible = false;
        PageContent.Content = null;
        _settingsView = null;
        if (_active is null) Welcome.Refresh(_settings, _ext);
    }

    void OpenSettings()
    {
        _settingsView = new SettingsView(_settings, _ext, new SettingsActions(
            Changed: ApplySettings,
            SetTheme: n => SetTheme(n),
            NewTheme: CreateCustomTheme,
            OpenSettingsJson: OpenSettingsJson,
            OpenConfigFolder: () => OpenInFileManager(Settings.ConfigDir),
            OpenThemesFolder: () => OpenInFileManager(Settings.ThemesDir),
            OpenExtensionsFolder: () => OpenInFileManager(ExtensionHost.UserDir),
            ReloadExtensions: () => { ReloadExtensions(); _settingsView?.Rebuild(); },
            ResetSettings: ResetSettings,
            OpenReleaseNotes: OpenChangelog));
        ShowPage("Settings", _settingsView);
    }

    void OpenSettingsJson()
    {
        if (!File.Exists(Settings.FilePath)) _settings.Save();
        HidePage();
        OpenFile(Settings.FilePath);
    }

    void OpenChangelog()
    {
        var renderer = new MarkdownRenderer(Path.GetDirectoryName(Pages.ChangelogPath() ?? ""), _ext, p => { HidePage(); OpenFile(p); }, m => Flash(m), null, false);
        ShowPage("Release notes", new ScrollViewer { Content = renderer.Render(Pages.ReadChangelog()), VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto });
    }

    // ------------------------------------------------- quick open & palette --

    async void ShowQuickOpen()
    {
        if (_root is null) { Flash("Open a folder first, then jump to any file in it."); return; }
        var root = _root;
        string[] files;
        try { files = await Task.Run(() => Core.ListFiles(root)); }
        catch (Exception ex) { CrashLog.Write(ex, fatal: false); return; }

        var open = _docs.Select(d => Path.GetRelativePath(root, d.Path).Replace('\\', '/')).ToHashSet();
        var items = files
            .OrderBy(f => open.Contains(f) ? 0 : 1).ThenBy(f => f.Count(c => c == '/')).ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Select(f => new PickItem(Path.GetFileName(f), f.Contains('/') ? f[..f.LastIndexOf('/')] : null, open.Contains(f) ? "open" : null, f, Path.GetFileName(f)))
            .ToList();
        Quick.Show($"Go to file in {Path.GetFileName(root.TrimEnd('/', '\\'))}…", items, item => OpenFile(Path.GetFullPath(Path.Combine(root, (string)item.Tag!))));
    }

    void ShowPalette()
    {
        var cmds = new List<(string Title, string? Hint, Action Run)>
        {
            ("Open Folder…", "Ctrl+O", () => _ = PickFolder()),
            ("New File", "Ctrl+N", BeginNewFile),
            ("Go to File…", "Ctrl+P", ShowQuickOpen),
            ("Save", "Ctrl+S", Save),
            ("Close Tab", "Ctrl+W", () => { if (_active is not null) Close(_active); }),
            ("Run Current File", "F5", RunActive),
            ("Stop Running Process", "Shift+F5", StopRunning),
            ("Toggle Sidebar", "Ctrl+B", () => SetSidebarVisible(!_sideVisible)),
            ("Toggle Terminal", "Ctrl+`", () => SetTerminalVisible(!TerminalVisible)),
            ("Toggle AI Agent Panel", "Ctrl+Shift+A", () => SetAgentVisible(!_agentVisible)),
            ("View: Explorer", "Ctrl+Shift+E", () => ShowView(SideView.Explorer)),
            ("View: Search", "Ctrl+Shift+F", () => ShowView(SideView.Search)),
            ("View: Web & npm", "Ctrl+Shift+W", () => ShowView(SideView.Web)),
            ("View: Extensions & Languages", "Ctrl+Shift+X", () => ShowView(SideView.Extensions)),
            ("Settings", "Ctrl+,", OpenSettings),
            ("Open settings.json", null, OpenSettingsJson),
            ("Color Theme…", null, ShowThemePicker),
            ("Language Mode…", null, ShowLanguagePicker),
            ("Create Custom Theme from Current", null, CreateCustomTheme),
            ("Open Themes Folder", null, () => OpenInFileManager(Settings.ThemesDir)),
            ("Open Extensions Folder", null, () => OpenInFileManager(ExtensionHost.UserDir)),
            ("Reload Extensions & Themes", null, ReloadExtensions),
            ("Markdown: Change Preview Layout", "Ctrl+Shift+V", CyclePreview),
            ("Toggle Word Wrap", null, () => { _settings.WordWrap = !_settings.WordWrap; _settings.Save(); ApplySettings(); _settingsView?.Rebuild(); }),
            ("Increase Editor Font Size", "Ctrl+=", () => ChangeFontSize(1)),
            ("Decrease Editor Font Size", "Ctrl+-", () => ChangeFontSize(-1)),
            ("Reset Editor Font Size", "Ctrl+0", () => ChangeFontSize(0)),
            ("License: Explain This File", null, ExplainThisFileAsLicense),
            ("Licenses: Browse the Guide…", null, ShowLicenseGuide),
            ("Release Notes", null, OpenChangelog),
            ("About LuminaIDE", null, OpenSettings),
        };
        if (_devUrl is { } url) cmds.Add(($"Open Dev Server ({url})", null, () => Platform.Open(url)));
        if (Web.Project is { } p)
            foreach (var (name, _) in p.Scripts.Where(s => NodeProjects.IsSafeScript(s.Name)))
                cmds.Add(($"{p.Manager}: run {name}", null, () => RunInTerminal(p.Dir, NodeProjects.RunCommand(p, name))));

        var items = cmds.Select(c => new PickItem(c.Title, null, Platform.Keys(c.Hint ?? ""), c.Run)).ToList();
        Quick.Show("Type a command…", items, item => ((Action)item.Tag!)());
    }

    /// <summary>Theme picker that previews each theme as you move through the list.</summary>
    void ShowThemePicker()
    {
        var original = ThemeManager.Current;
        var themes = _ext.Themes.OrderBy(t => t.Name == original.Name ? 0 : 1).ToList(); // current first, so opening doesn't change anything
        var items = themes.Select(t => new PickItem(t.Name, t.IsLight ? "Light" : "Dark", t.Name == original.Name ? "current" : null, t)).ToList();

        Action<PickItem?>? highlighted = null;
        Action<bool>? closed = null;
        highlighted = it => { if (it?.Tag is AppTheme th && th.Name != ThemeManager.Current.Name) ThemeManager.Apply(th); };
        closed = picked =>
        {
            Quick.Highlighted -= highlighted;
            Quick.Closed -= closed;
            if (!picked) ThemeManager.Apply(original);
        };
        Quick.Highlighted += highlighted;
        Quick.Closed += closed;
        Quick.Show("Select a color theme (↑↓ to preview)", items, it => SetTheme(((AppTheme)it.Tag!).Name));
    }

    void ShowLanguagePicker()
    {
        if (_active is not { } d) { Flash("Open a file first."); return; }
        var items = new List<PickItem> { new("Plain Text", null, d.LanguageId is null ? "current" : null, null) };
        items.AddRange(_ext.Languages.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).Select(l => new PickItem(l.Name, l.Id, l.Id == d.LanguageId ? "current" : null, l.Id)));
        Quick.Show($"Language for {d.Title}", items, it => SetLanguage(d, it.Tag as string));
    }

    // ------------------------------------------------------------- licenses --

    /// <summary>Tells you, above a license file, which license it is and what it allows.</summary>
    void ShowLicense(Doc d)
    {
        var id = Core.DetectLicense(d.Editor.Text);
        var info = id is null ? null : _ext.Licenses.FirstOrDefault(l => l.Id == id);
        if (d.BannerDismissed) { d.BannerSlot.IsVisible = false; return; }

        d.BannerSlot.Content = LicenseCards.Banner(info, d.BannerCollapsed,
            collapsed => { d.BannerCollapsed = collapsed; ShowLicense(d); },
            () => { d.BannerDismissed = true; d.BannerSlot.IsVisible = false; },
            Platform.Open);
        d.BannerSlot.IsVisible = true;
    }

    void ScheduleLicense(Doc d)
    {
        _pendingLicense.Add(d);
        _licenseTimer.Stop();
        _licenseTimer.Start();
    }

    /// <summary>Works on any open file: if its text is a known license, show the explanation (even if the name doesn't say LICENSE).</summary>
    void ExplainThisFileAsLicense()
    {
        if (_active is not { } d) { Flash("Open a license file first."); return; }
        if (!d.IsLicense && Core.DetectLicense(d.Editor.Text) is null) { Flash($"{d.Title} doesn't match a license LuminaIDE knows."); return; }
        d.IsLicense = true;
        d.BannerDismissed = false;
        ShowLicense(d);
    }

    /// <summary>Pick any built-in (or extension-supplied) license and read what it allows.</summary>
    void ShowLicenseGuide()
    {
        var items = _ext.Licenses.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .Select(l => new PickItem(l.Name, l.Category, l.Id, l)).ToList();
        Quick.Show("Which license do you want to read about?", items, it =>
        {
            var info = (LicenseInfo)it.Tag!;
            ShowPage(info.Name, LicenseCards.Page(info, Platform.Open));
        });
    }

    // ---------------------------------------------------------- web & run --

    void InitWeb()
    {
        Web.Root = () => _root;
        Web.ActiveDir = () => _active is null ? null : Path.GetDirectoryName(_active.Path);
        Web.RunInTerminal = RunInTerminal;
        Web.OpenFile = p => OpenFile(p);
        Web.TreeChanged = LoadTree;
        StatusDev.Tapped += (_, _) => { if (_devUrl is { } u) Platform.Open(u); };
        StatusDev.PointerPressed += (_, e) => { if (e.GetCurrentPoint(StatusDev).Properties.IsRightButtonPressed) SetDevUrl(null); };
    }

    /// <summary>Sends "cd dir && command" to the terminal panel, starting the shell first if needed.</summary>
    void RunInTerminal(string dir, string command)
    {
        bool fresh = _term is null || !_termTimer.IsEnabled; // first use, or the shell had exited
        if (fresh) StartTerminal();
        SetTerminalVisible(true);

        var line = $"{Platform.CdCommand(dir)} && {command}\n";
        // A brand-new shell needs a moment to print its first prompt; input sent earlier gets echoed twice.
        if (fresh) DispatcherTimer.RunOnce(() => _term?.Send(line), TimeSpan.FromMilliseconds(400));
        else _term?.Send(line);
    }

    void StopRunning()
    {
        _term?.Send("\x03");
        SetDevUrl(null);
    }

    /// <summary>Picks up "http://localhost:5173/"-style addresses printed by dev servers (Vite, webpack, Next...).</summary>
    void SniffDevUrl(string text)
    {
        var matches = DevUrlPattern.Matches(text);
        if (matches.Count == 0) return;
        SetDevUrl(matches[^1].Value.TrimEnd('.', ',', ';', ':').Replace("0.0.0.0", "localhost"));
    }

    void SetDevUrl(string? url)
    {
        if (_devUrl == url) return;
        _devUrl = url;
        StatusDev.IsVisible = url is not null;
        StatusDevText.Text = url is null ? "" : "⚡ " + url.Replace("https://", "").Replace("http://", "").TrimEnd('/');
        Web.SetDevUrl(url);
    }
}
