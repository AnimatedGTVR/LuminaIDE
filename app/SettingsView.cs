using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace LuminaIDE;

/// <summary>The commands the settings page can trigger in the window.</summary>
public sealed record SettingsActions(
    Action Changed, Action<string> SetTheme, Action NewTheme, Action OpenSettingsJson, Action OpenConfigFolder,
    Action OpenThemesFolder, Action OpenExtensionsFolder, Action ReloadExtensions, Action ResetSettings, Action OpenReleaseNotes);

/// <summary>Settings page: every option from settings.json, applied and saved as you change it.</summary>
public sealed class SettingsView : UserControl
{
    readonly Settings _s;
    readonly ExtensionHost _ext;
    readonly SettingsActions _a;
    readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };

    public SettingsView(Settings settings, ExtensionHost ext, SettingsActions actions)
    {
        _s = settings; _ext = ext; _a = actions;
        Content = _scroll;
        Rebuild();
    }

    void Changed()
    {
        _s.Normalize();
        _s.Save();
        _a.Changed();
    }

    /// <summary>Redraws the page (keeping the scroll position) so chips and values reflect the settings.</summary>
    public void Rebuild()
    {
        var offset = _scroll.Offset;
        var page = new StackPanel { MaxWidth = 740, Margin = new Thickness(40, 30, 40, 70), HorizontalAlignment = HorizontalAlignment.Left };

        page.Children.Add(new TextBlock { Text = "Settings", FontSize = 30, FontWeight = FontWeight.Bold, LetterSpacing = -0.5 });
        var sub = new TextBlock { Text = "Changes apply immediately and are saved to settings.json.", Margin = new Thickness(0, 6, 0, 0) };
        sub.Classes.Add("muted");
        page.Children.Add(sub);

        // ---- Appearance ----
        var appearance = new Form.Card("Appearance");
        var themes = new WrapPanel { Margin = new Thickness(0, 16, 0, 6) };
        foreach (var t in _ext.Themes) themes.Children.Add(ThemeChip(t));
        appearance.Add(themes);
        var themeButtons = new WrapPanel { Margin = new Thickness(0, 12, 0, 4) };
        themeButtons.Children.Add(Form.Button("New theme from the current one", _a.NewTheme));
        themeButtons.Children.Add(Form.Button("Open themes folder", _a.OpenThemesFolder));
        appearance.Add(themeButtons);
        appearance.AddTo(page);

        // ---- Editor ----
        var editor = new Form.Card("Editor");
        var font = new TextBox { Text = _s.EditorFontFamily, Width = 230, Watermark = "JetBrains Mono (built in)" };
        void CommitFont() { if (font.Text?.Trim() != _s.EditorFontFamily) { _s.EditorFontFamily = font.Text?.Trim() ?? ""; Changed(); } }
        font.LostFocus += (_, _) => CommitFont();
        font.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) CommitFont(); };
        editor.Add(Form.Row("Font family", "Any installed font, e.g. Fira Code. Falls back to the built-in JetBrains Mono.", font));
        editor.Add(Form.Row("Font size", null, Form.Stepper(_s.EditorFontSize, 8, 40, 1, v => { _s.EditorFontSize = v; Changed(); })));
        editor.Add(Form.Row("Tab size", null, Form.Stepper(_s.TabSize, 1, 16, 1, v => { _s.TabSize = (int)v; Changed(); }, "0")));
        editor.Add(Form.Row("Insert spaces for Tab", null, Form.Toggle(_s.InsertSpaces, v => { _s.InsertSpaces = v; Changed(); })));
        editor.Add(Form.Row("Word wrap", null, Form.Toggle(_s.WordWrap, v => { _s.WordWrap = v; Changed(); })));
        editor.Add(Form.Row("Line numbers", null, Form.Toggle(_s.LineNumbers, v => { _s.LineNumbers = v; Changed(); })));
        editor.Add(Form.Row("Highlight current line", null, Form.Toggle(_s.HighlightCurrentLine, v => { _s.HighlightCurrentLine = v; Changed(); })));
        editor.Add(Form.Row("Auto save", "Never lose work: save after a pause in typing, or when you switch away.",
            Form.ChipRow([("Off", "off"), ("After a delay", "afterDelay"), ("When focus leaves", "onFocusLost")], _s.AutoSave, v => { _s.AutoSave = v; Changed(); Rebuild(); })));
        editor.Add(Form.Row("Trim trailing whitespace on save", null, Form.Toggle(_s.TrimTrailingWhitespace, v => { _s.TrimTrailingWhitespace = v; Changed(); })));
        editor.Add(Form.Row("Ensure a final newline on save", null, Form.Toggle(_s.InsertFinalNewline, v => { _s.InsertFinalNewline = v; Changed(); })));
        editor.AddTo(page);

        // ---- Markdown / terminal / agent ----
        var more = new Form.Card("Markdown, terminal & agent");
        more.Add(Form.Row("Open Markdown files as", null,
            Form.ChipRow([("Side by side", "split"), ("Preview only", "preview"), ("Editor only", "editor")], _s.MarkdownMode, v => { _s.MarkdownMode = v; Changed(); Rebuild(); })));
        more.Add(Form.Row("Show images from the web", "Badges and screenshots in READMEs. Loading one contacts the site that hosts it, so turn this off if you'd rather not.",
            Form.Toggle(_s.MarkdownRemoteImages, v => { _s.MarkdownRemoteImages = v; Changed(); })));
        more.Add(Form.Row("Play animated GIFs", "Click a GIF in the preview to pause it. Off shows just the first frame.",
            Form.Toggle(_s.MarkdownAnimateGifs, v => { _s.MarkdownAnimateGifs = v; Changed(); })));
        more.Add(Form.Row("Terminal font size", null, Form.Stepper(_s.TerminalFontSize, 8, 32, 0.5, v => { _s.TerminalFontSize = v; Changed(); })));
        more.Add(Form.Row("Default AI agent", "Runs the Claude Code or Codex CLI you already have installed.",
            Form.ChipRow([("Claude Code", "claude"), ("Codex", "codex")], _s.Agent, v => { _s.Agent = v; Changed(); Rebuild(); })));
        more.Add(Form.Row("Agent mode", "Plan mode can read your files but not change them.",
            Form.ChipRow([("Plan · read-only", "read"), ("Edit files", "edit")], _s.AgentMode, v => { _s.AgentMode = v; Changed(); Rebuild(); })));
        more.AddTo(page);

        // ---- Files ----
        var files = new Form.Card("Files & configuration");
        var buttons = new WrapPanel { Margin = new Thickness(0, 14, 0, 6) };
        buttons.Children.Add(Form.Button("Open settings.json", _a.OpenSettingsJson, primary: true));
        buttons.Children.Add(Form.Button("Open config folder", _a.OpenConfigFolder));
        buttons.Children.Add(Form.Button("Open extensions folder", _a.OpenExtensionsFolder));
        buttons.Children.Add(Form.Button("Reload extensions & themes", _a.ReloadExtensions));
        buttons.Children.Add(Form.Button("Reset all settings", _a.ResetSettings));
        var path = new TextBlock { Text = Platform.Tilde(Settings.FilePath), FontSize = 11.5, Margin = new Thickness(0, 0, 0, 12) };
        path.Classes.Add("muted");
        files.Add(new StackPanel { Children = { buttons, path } });
        files.AddTo(page);

        // ---- About ----
        var about = new Form.Card("About");
        about.Add(About());
        about.AddTo(page);

        _scroll.Content = page;
        _scroll.Offset = offset;
    }

    Control ThemeChip(Theme t)
    {
        var swatches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        foreach (var key in new[] { "background", "accent" })
            swatches.Children.Add(new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Opaque(t.UiColor(key))), BorderBrush = new SolidColorBrush(Color.FromArgb(70, 128, 128, 128)), BorderThickness = new Thickness(1) });
        foreach (var k in new[] { "keyword", "string" })
            swatches.Children.Add(new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Color.Parse(t.Syntax.GetValueOrDefault(k) ?? "#888888")) });

        var label = t.Name;
        var chip = new Border { Margin = new Thickness(0, 0, 8, 8), Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { swatches, new TextBlock { Text = label } } } };
        chip.Classes.Add("chip");
        chip.Classes.Set("active", t.Name == ThemeManager.Current.Name);
        var name = t.Name;
        chip.Tapped += (_, _) => { _a.SetTheme(name); Rebuild(); };
        return chip;
    }

    static Color Opaque(Color c) => Color.FromArgb(255, c.R, c.G, c.B);

    Control About()
    {
        var box = new StackPanel { Spacing = 4, Margin = new Thickness(0, 14, 0, 6) };
        box.Children.Add(new TextBlock { Text = $"{AppInfo.Name} {AppInfo.Version}", FontWeight = FontWeight.SemiBold });
        foreach (var line in new[]
        {
            $"{Platform.Name} · .NET {Environment.Version} · {_ext.Languages.Count} languages · {_ext.Themes.Count} themes",
            "Released under the MIT License. Built with Avalonia, AvaloniaEdit, Rust and C++ (see NOTICE for third-party licenses).",
        })
        {
            var t = new TextBlock { Text = line, FontSize = 12, TextWrapping = TextWrapping.Wrap };
            t.Classes.Add("muted");
            box.Children.Add(t);
        }
        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(Form.Button("Release notes", _a.OpenReleaseNotes));
        box.Children.Add(buttons);
        return box;
    }
}
