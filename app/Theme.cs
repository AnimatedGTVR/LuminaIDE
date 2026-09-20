using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace LuminaIDE;

/// <summary>Token kinds in the order the Rust tokenizer numbers them.</summary>
public static class SyntaxKinds
{
    public static readonly string[] Names =
        ["keyword", "control", "type", "function", "definition", "property", "string", "number", "comment", "constant", "attribute"];
}

public sealed class Theme
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "dark";
    [JsonPropertyName("ui")] public Dictionary<string, string> Ui { get; set; } = [];
    [JsonPropertyName("syntax")] public Dictionary<string, string> Syntax { get; set; } = [];

    public bool IsLight => Type.Equals("light", StringComparison.OrdinalIgnoreCase);

    /// <summary>Used when no extension supplies a theme, and to fill gaps in partial themes.</summary>
    public static Theme Fallback { get; } = new()
    {
        Name = "Vanta Night",
        Ui = new()
        {
            ["background"] = "#151821", ["sidebar"] = "#10131B", ["activityBar"] = "#0C0F16", ["panel"] = "#10131B",
            ["input"] = "#0C1018", ["border"] = "#2B3141", ["foreground"] = "#E8ECF4", ["muted"] = "#A0ABBE",
            ["accent"] = "#AC9BFF", ["hover"] = "#242A39", ["selection"] = "#383654", ["lineHighlight"] = "#1C2130",
            ["lineNumber"] = "#77839A", ["statusBar"] = "#10131B", ["statusBarText"] = "#A9B5C9",
        },
        Syntax = new()
        {
            ["keyword"] = "#ffb07c", ["control"] = "#feaca9", ["type"] = "#fea4e8", ["function"] = "#ffc9a8",
            ["definition"] = "#afbfff", ["property"] = "#d7d7e6", ["string"] = "#92cfa5", ["number"] = "#f1b595",
            ["comment"] = "#82819c", ["constant"] = "#f1b595", ["attribute"] = "#afbfff",
        },
    };

    public Color UiColor(string key)
    {
        var color = Parse(Ui.GetValueOrDefault(key) ?? Fallback.Ui.GetValueOrDefault(key) ?? "#ff00ff");
        return Color.FromRgb(color.R, color.G, color.B);
    }

    /// <summary>The Vanta Night colour for a UI key (what a partial theme falls back to).</summary>
    public Color UiFallback(string key) => Parse(Fallback.Ui.GetValueOrDefault(key) ?? "#ff00ff");

    public IBrush[] SyntaxBrushes() =>
        SyntaxKinds.Names.Select(k => (IBrush)new SolidColorBrush(
            Parse(Syntax.GetValueOrDefault(k) ?? Fallback.Syntax.GetValueOrDefault(k) ?? Ui.GetValueOrDefault("foreground") ?? "#ffffff")))
            .ToArray();

    static Color Parse(string s) => Color.TryParse(s, out var c) ? c : Colors.Magenta;

    public static Theme? Load(string path)
    {
        try { return JsonSerializer.Deserialize<Theme>(File.ReadAllText(path)); }
        catch { return null; }
    }
}

/// <summary>Applies a theme by (re)writing application resources; every control binds them dynamically.</summary>
public static class ThemeManager
{
    public static Theme Current { get; private set; } = Theme.Fallback;
    public static IBrush[] SyntaxBrushes { get; private set; } = Theme.Fallback.SyntaxBrushes();
    public static event Action? Changed;

    public static void Apply(Theme theme)
    {
        Current = theme;
        SyntaxBrushes = theme.SyntaxBrushes();
        var app = Application.Current!;
        app.RequestedThemeVariant = theme.IsLight ? ThemeVariant.Light : ThemeVariant.Dark;

        IBrush B(string key) => new SolidColorBrush(theme.UiColor(key));
        var bg = B("background"); var side = B("sidebar"); var input = B("input"); var border = B("border");
        var fg = B("foreground"); var muted = B("muted"); var accent = B("accent");
        var hover = B("hover"); var sel = B("selection");

        // Our own keys, referenced by App.axaml styles and the views.
        var r = app.Resources;
        r["Bg"] = bg; r["BgSidebar"] = side; r["BgActivity"] = B("activityBar"); r["BgPanel"] = B("panel");
        r["BgInput"] = input; r["BgHover"] = hover; r["BgSelection"] = sel; r["BgLine"] = B("lineHighlight");
        r["BgStatus"] = B("statusBar"); r["FgStatus"] = B("statusBarText");
        // Choose black or white text with the better contrast against an accent button.
        var accentColor = theme.UiColor("accent");
        static double Linear(byte channel) { double c = channel / 255.0; return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
        double luminance = 0.2126 * Linear(accentColor.R) + 0.7152 * Linear(accentColor.G) + 0.0722 * Linear(accentColor.B);
        r["FgOnAccent"] = luminance > 0.179 ? Brushes.Black : Brushes.White;
        r["Border"] = border; r["Fg"] = fg; r["FgMuted"] = muted; r["Accent"] = accent; r["LineNumber"] = B("lineNumber");
        var sideColor = theme.UiColor("sidebar");
        r["BgFloating"] = new SolidColorBrush(Color.FromArgb(Math.Max(sideColor.A, (byte)0xF2), sideColor.R, sideColor.G, sideColor.B));
        r["FolderFill"] = new SolidColorBrush(theme.UiColor("accent")) { Opacity = 0.30 };
        r["AccentSoft"] = new SolidColorBrush(theme.UiColor("accent")) { Opacity = 0.18 };

        // Accent details and solid card surfaces.
        var accent2 = Color.TryParse(theme.Syntax.GetValueOrDefault("type") ?? theme.Syntax.GetValueOrDefault("keyword") ?? "", out var a2) ? a2 : theme.UiColor("accent");
        r["Accent2"] = new SolidColorBrush(accent2);
        r["TileBg"] = side;
        r["TileBgHover"] = hover;
        r["TileBorder"] = border;

        // Fluent control resources we rely on (text boxes, lists, tree, menus, scrollbars).
        foreach (var k in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused" }) r[k] = input;
        foreach (var k in new[] { "TextControlBorderBrush", "TextControlBorderBrushPointerOver" }) r[k] = border;
        r["TextControlBorderBrushFocused"] = accent;
        foreach (var k in new[] { "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused" }) r[k] = fg;
        r["TextControlPlaceholderForeground"] = muted; r["TextControlPlaceholderForegroundPointerOver"] = muted; r["TextControlPlaceholderForegroundFocused"] = muted;
        r["TextControlSelectionHighlightColor"] = theme.UiColor("selection");

        foreach (var k in new[] { "TreeViewItemBackgroundSelected", "TreeViewItemBackgroundSelectedPointerOver", "TreeViewItemBackgroundPressed" }) r[k] = sel;
        r["TreeViewItemBackgroundPointerOver"] = hover;
        foreach (var k in new[] { "TreeViewItemForeground", "TreeViewItemForegroundPointerOver", "TreeViewItemForegroundSelected", "TreeViewItemForegroundSelectedPointerOver", "TreeViewItemForegroundPressed" }) r[k] = fg;

        foreach (var k in new[] { "ListBoxItemBackgroundSelected", "ListBoxItemBackgroundSelectedPointerOver", "ListBoxItemBackgroundSelectedPressed" }) r[k] = sel;
        foreach (var k in new[] { "ListBoxItemBackgroundPointerOver", "ListBoxItemBackgroundPressed" }) r[k] = hover;
        foreach (var k in new[] { "ListBoxItemForeground", "ListBoxItemForegroundPointerOver", "ListBoxItemForegroundSelected", "ListBoxItemForegroundSelectedPointerOver", "ListBoxItemForegroundPressed" }) r[k] = fg;

        foreach (var k in new[] { "MenuFlyoutPresenterBackground", "ContextMenuBackground" }) r[k] = side;
        r["MenuFlyoutPresenterBorderBrush"] = border; r["ContextMenuBorderBrush"] = border;
        r["MenuFlyoutItemBackgroundPointerOver"] = hover; r["MenuFlyoutItemBackgroundPressed"] = sel;
        r["MenuFlyoutItemForeground"] = fg; r["MenuFlyoutItemForegroundPointerOver"] = fg;

        r["ScrollBarThumbFill"] = muted; r["ScrollBarThumbFillPointerOver"] = muted; r["ScrollBarThumbFillPressed"] = fg;
        r["ScrollBarTrackFill"] = Brushes.Transparent; r["ScrollBarTrackFillPointerOver"] = Brushes.Transparent;

        Changed?.Invoke();
    }
}
