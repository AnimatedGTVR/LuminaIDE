using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;

namespace LuminaIDE;

/// <summary>Small helpers for building the clickable rows the styles in App.axaml expect.</summary>
static class Ui
{
    public static Border Row(string title, string? hint, Action onTap)
    {
        var grid = new Grid { ColumnDefinitions = new("*,Auto") };
        grid.Children.Add(new TextBlock { Text = title, TextTrimming = TextTrimming.CharacterEllipsis });
        if (hint is not null)
        {
            var h = new TextBlock { Text = hint, FontSize = 12, MaxWidth = 190, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(12, 0, 0, 0) };
            h.Classes.Add("muted");
            Grid.SetColumn(h, 1);
            grid.Children.Add(h);
        }
        var row = new Border { Child = grid };
        row.Classes.Add("item");
        row.Tapped += (_, _) => onTap();
        return row;
    }
}

public partial class WelcomeView : UserControl
{
    static readonly string[] Tips =
    [
        "Press Ctrl+P to jump to any file, and Ctrl+Shift+P for every command.",
        "F5 runs the current file in the terminal — HTML files open in your browser.",
        "Open a Markdown file to get a live preview beside it (Ctrl+Shift+V cycles the layout).",
        "Drop a JSON file into your themes folder to add a custom theme — Settings can start one for you.",
        "Ask Claude Code or Codex about your project with Ctrl+Shift+A. Plan mode is read-only.",
        "Working on a website? The Web panel runs your npm scripts and links straight to the dev server.",
        "Everything is in settings.json — comments are allowed, and changes apply when you save it.",
    ];

    // Tiles: icon path (24x24), title, subtitle, action index.
    const string IconFolder = "M3.5,6.5 L3.5,18.5 L20.5,18.5 L20.5,8.5 L11.5,8.5 L9.5,6.5 Z";
    const string IconFile = "M6,3.5 H14 L18.5,8 V20.5 H6 Z M14,3.5 V8 H18.5 M12,11 V17 M9,14 H15";
    const string IconGlobe = "M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 Z M3,12 H21 M12,3 C8,7 8,17 12,21 M12,3 C16,7 16,17 12,21";
    const string IconSpark = "M12,3.5 L13.9,9.6 L20,11.5 L13.9,13.4 L12,19.5 L10.1,13.4 L4,11.5 L10.1,9.6 Z";
    const string IconSliders = "M4,7 H20 M4,12 H20 M4,17 H20 M8,5 A2,2 0 1 0 8,9 A2,2 0 1 0 8,5 Z M15,10 A2,2 0 1 0 15,14 A2,2 0 1 0 15,10 Z M10,15 A2,2 0 1 0 10,19 A2,2 0 1 0 10,15 Z";
    const string IconCommand = "M9,9 H15 V15 H9 Z M9,9 V6.5 A2.5,2.5 0 1 0 6.5,9 H9 M15,9 V6.5 A2.5,2.5 0 1 1 17.5,9 H15 M9,15 V17.5 A2.5,2.5 0 1 1 6.5,15 H9 M15,15 V17.5 A2.5,2.5 0 1 0 17.5,15 H15";

    public event Action? OpenFolderRequested, NewFileRequested, NewWebsiteRequested, AgentRequested, SettingsRequested, PaletteRequested, ReleaseNotesRequested;
    public event Action<string>? RecentRequested, ThemeRequested;


    public WelcomeView()
    {
        AvaloniaXamlLoader.Load(this);
        Ctl<TextBlock>("VersionText").Text = "v" + AppInfo.Version;
        Ctl<TextBlock>("PlatformText").Text = Platform.Name;

        var tiles = Ctl<UniformGrid>("Tiles");
        tiles.Children.Add(Tile(IconFolder, "Open folder", "Start on a project", "Ctrl+O", () => OpenFolderRequested?.Invoke()));
        tiles.Children.Add(Tile(IconFile, "New file", "In the open folder", "Ctrl+N", () => NewFileRequested?.Invoke()));
        tiles.Children.Add(Tile(IconGlobe, "New website", "Vite app or plain HTML", "", () => NewWebsiteRequested?.Invoke()));
        tiles.Children.Add(Tile(IconSpark, "Ask an AI agent", "Claude Code or Codex", "Ctrl+Shift+A", () => AgentRequested?.Invoke()));
        tiles.Children.Add(Tile(IconSliders, "Settings", "Fonts, themes, behavior", "Ctrl+,", () => SettingsRequested?.Invoke()));
        tiles.Children.Add(Tile(IconCommand, "Command palette", "Run anything by name", "Ctrl+Shift+P", () => PaletteRequested?.Invoke()));

    }

    T Ctl<T>(string name) where T : Control => this.FindControl<T>(name)!;

    static Border Tile(string icon, string title, string subtitle, string shortcut, Action tap)
    {
        var glyph = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(icon), Width = 24, Height = 24, Stretch = Stretch.None, StrokeThickness = 1.6, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round };
        glyph.Classes.Add("tile-icon");
        var top = new Grid { ColumnDefinitions = new("Auto,*") };
        top.Children.Add(glyph);
        if (shortcut.Length > 0)
        {
            var k = new TextBlock { Text = Platform.Keys(shortcut), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
            k.Classes.Add("muted");
            Grid.SetColumn(k, 1);
            top.Children.Add(k);
        }
        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(top);
        stack.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 14.5, Margin = new Thickness(0, 6, 0, 0) });
        var sub = new TextBlock { Text = subtitle, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        sub.Classes.Add("muted");
        stack.Children.Add(sub);

        var tile = new Border { Child = stack, Margin = new Thickness(6) };
        tile.Classes.Add("tile");
        tile.Tapped += (_, _) => tap();
        return tile;
    }

    // Content

    /// <summary>Rebuilds everything that depends on the theme, recents or release notes.</summary>
    public void Refresh(Settings settings, ExtensionHost ext)
    {
        var t = ThemeManager.Current;
        var accent = t.UiColor("accent");
        var second = Color.TryParse(t.Syntax.GetValueOrDefault("type") ?? "", out var c2) ? c2 : accent;
        var fg = t.UiColor("foreground");

        Ctl<Border>("Logo").Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(accent, 0), new GradientStop(second, 1) },
        };
        Ctl<TextBlock>("Title").Foreground = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(255, fg.R, fg.G, fg.B), 0), new GradientStop(accent, 1) },
        };
        Ctl<ScrollViewer>("Scroller").ScrollToHome();

        // Recent folders
        var recent = Ctl<StackPanel>("RecentList");
        recent.Children.Clear();
        foreach (var folder in settings.RecentFolders.Where(Directory.Exists).Take(6))
        {
            var name = System.IO.Path.GetFileName(folder.TrimEnd('/', '\\'));
            var parent = System.IO.Path.GetDirectoryName(folder.TrimEnd('/', '\\')) ?? "";
            recent.Children.Add(Ui.Row(name, Platform.Tilde(parent), () => RecentRequested?.Invoke(folder)));
        }
        Ctl<TextBlock>("NoRecent").IsVisible = recent.Children.Count == 0;

        // What's new (from CHANGELOG.md)
        var news = Ctl<StackPanel>("WhatsNew");
        news.Children.Clear();
        var (heading, items) = Pages.LatestRelease();
        news.Children.Add(new TextBlock { Text = heading, FontWeight = FontWeight.SemiBold, FontSize = 14 });
        foreach (var item in items.Take(3))
        {
            var line = new TextBlock { Text = "•  " + item, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };
            news.Children.Add(line);
        }
        var more = new TextBlock { Text = "Read all release notes →", FontSize = 12.5, Margin = new Thickness(0, 4, 0, 0), Cursor = new Cursor(StandardCursorType.Hand) };
        more.Classes.Add("accent");
        more.Tapped += (_, _) => ReleaseNotesRequested?.Invoke();
        news.Children.Add(more);

        // Themes
        var chips = Ctl<WrapPanel>("ThemeChips");
        chips.Children.Clear();
        foreach (var th in ext.Themes)
        {
            var swatches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            foreach (var key in new[] { "background", "accent" })
            {
                var c = th.UiColor(key);
                swatches.Children.Add(new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Color.FromArgb(255, c.R, c.G, c.B)), BorderBrush = new SolidColorBrush(Color.FromArgb(70, 128, 128, 128)), BorderThickness = new Thickness(1) });
            }
            foreach (var k in new[] { "keyword", "string" })
                swatches.Children.Add(new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Color.Parse(th.Syntax.GetValueOrDefault(k) ?? "#888888")) });

            var chip = new Border { Margin = new Thickness(0, 0, 8, 8), Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { swatches, new TextBlock { Text = th.Name } } } };
            chip.Classes.Add("chip");
            chip.Classes.Set("active", th.Name == ThemeManager.Current.Name);
            var name = th.Name;
            chip.Tapped += (_, _) => ThemeRequested?.Invoke(name);
            chips.Children.Add(chip);
        }

        Ctl<TextBlock>("Tip").Text = "Tip · " + Tips[(DateTime.Now.DayOfYear + settings.RecentFolders.Count) % Tips.Length];
        Ctl<TextBlock>("Summary").Text =
            $"{ext.Languages.Count} languages · {ext.Themes.Count} themes · {ext.Licenses.Count} licenses · {ext.Extensions.Count} extensions   ·   config: {Platform.Tilde(Settings.ConfigDir)}";
    }
}
