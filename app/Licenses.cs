using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using ShapePath = Avalonia.Controls.Shapes.Path;
using Avalonia.Layout;
using Avalonia.Media;

namespace LuminaIDE;

/// <summary>A license definition from an extension's license file (see docs/LICENSES.md).</summary>
public sealed class LicenseInfo
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("category")] public string Category { get; set; } = "";
    [JsonPropertyName("summary")] public string Summary { get; set; } = "";
    [JsonPropertyName("permissions")] public List<string> Permissions { get; set; } = [];
    [JsonPropertyName("conditions")] public List<string> Conditions { get; set; } = [];
    [JsonPropertyName("limitations")] public List<string> Limitations { get; set; } = [];
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
    [JsonPropertyName("url")] public string Url { get; set; } = "";
}

/// <summary>Which files are license files: LICENSE, COPYING, UNLICENSE, LICENSE-MIT, OFL-*.txt, or any text file in a licenses/ folder.</summary>
public static class LicenseFiles
{
    static readonly Regex StrongName = new(@"^(licen[cs]e|copying|unlicen[cs]e|ofl)([-_. ].*)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
        { "", ".txt", ".md", ".markdown", ".rst", ".text", ".lesser", ".apache", ".mit", ".bsd", ".gpl", ".mpl", ".ofl" };

    public static bool IsLicenseFile(string path)
    {
        var name = System.IO.Path.GetFileName(path);
        if (!TextExtensions.Contains(System.IO.Path.GetExtension(name))) return false; // license-checker.js, LICENSE.rs...
        if (StrongName.IsMatch(name)) return true;
        var folder = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path) ?? "");
        return folder.Equals("licenses", StringComparison.OrdinalIgnoreCase) || folder.Equals("licences", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Plain-language wording for the permission / condition / limitation tags used in license files.</summary>
public static class LicenseTerms
{
    public static readonly IReadOnlyDictionary<string, (string Label, string Help)> Permissions = new Dictionary<string, (string, string)>
    {
        ["commercial-use"] = ("Use commercially", "You can use it in commercial projects and products, including ones you sell."),
        ["modifications"] = ("Modify", "You can change the code."),
        ["distribution"] = ("Distribute", "You can share the original or your modified version with others."),
        ["private-use"] = ("Use privately", "You can use and modify it for yourself without sharing anything."),
        ["patent-use"] = ("Patent grant", "Contributors give you a license to any patents that cover their contributions."),
    };

    public static readonly IReadOnlyDictionary<string, (string Label, string Help)> Conditions = new Dictionary<string, (string, string)>
    {
        ["include-copyright"] = ("Keep the copyright notice", "When you share it, include the original copyright notice and license text."),
        ["include-copyright--source"] = ("Keep the notice (source only)", "Keep the notice when you share source code; compiled programs don't need it."),
        ["document-changes"] = ("State your changes", "Say clearly what you changed from the original."),
        ["disclose-source"] = ("Share the source", "When you distribute it, make the source code available too."),
        ["network-use-disclose"] = ("Share source for network use", "If people use your modified version over a network (a web service), you must offer them the source."),
        ["same-license"] = ("Same license", "Your version, and anything built on it, must be released under this same license."),
        ["same-license--file"] = ("Same license (per file)", "Changes to the licensed files must stay under this license; your other files can use any license."),
        ["same-license--library"] = ("Same license (the library)", "Changes to the library itself must stay under this license; programs that merely use it can use any license."),
    };

    public static readonly IReadOnlyDictionary<string, (string Label, string Help)> Limitations = new Dictionary<string, (string, string)>
    {
        ["liability"] = ("No liability", "The authors aren't responsible for damage caused by using the software."),
        ["warranty"] = ("No warranty", "It comes as-is, with no promise that it works or is fit for any purpose."),
        ["trademark-use"] = ("No trademark rights", "The license doesn't let you use the authors' names, logos or trademarks."),
        ["patent-use"] = ("No patent grant", "The license does not give you any rights under the authors' patents."),
    };

    /// <summary>Label and explanation for a tag, falling back to the raw tag for ones added by a user's own license file.</summary>
    public static (string Label, string Help) Describe(IReadOnlyDictionary<string, (string Label, string Help)> table, string tag) =>
        table.TryGetValue(tag, out var t) ? t : (tag, "");
}

/// <summary>Builds the license banner (over a license file) and the full license page.</summary>
public static class LicenseCards
{
    static readonly Color Green = Color.Parse("#3FB37F"), Amber = Color.Parse("#E0A63A"), Red = Color.Parse("#E5675F");

    const string IconLicense = "M6,3.5 H14 L18.5,8 V20.5 H6 Z M14,3.5 V8 H18.5 M9,14.2 L11.2,16.4 L15.2,12";
    public const string UnrecognizedSummary =
        "This looks like a license file, but it isn't one LuminaIDE knows. Licenses differ a lot in what they allow, so read it carefully. If you're unsure what it means for you, ask the author or a lawyer.";
    public const string Disclaimer = "Plain-language summary, not legal advice. The license text itself is what actually applies.";

    static Control Chip(string label, string help, Color tint)
    {
        var dot = new Ellipse { Width = 6, Height = 6, Fill = new SolidColorBrush(tint), VerticalAlignment = VerticalAlignment.Center };
        var text = new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        var chip = new Border
        {
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = { dot, text } },
            Background = new SolidColorBrush(tint, 0.14), BorderBrush = new SolidColorBrush(tint, 0.38), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 3), Margin = new Thickness(0, 0, 6, 6),
        };
        if (help.Length > 0) ToolTip.SetTip(chip, help);
        return chip;
    }

    static Control Group(string title, IEnumerable<Control> chips)
    {
        var grid = new Grid { ColumnDefinitions = new("112,*"), Margin = new Thickness(0, 2) };
        var label = new TextBlock { Text = title, FontSize = 12, Margin = new Thickness(0, 4, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        label.Classes.Add("muted");
        var wrap = new WrapPanel();
        foreach (var c in chips) wrap.Children.Add(c);
        Grid.SetColumn(wrap, 1);
        grid.Children.Add(label);
        grid.Children.Add(wrap);
        return grid;
    }

    static IEnumerable<Control> Chips(IEnumerable<string> tags, IReadOnlyDictionary<string, (string Label, string Help)> table, Color tint) =>
        tags.Select(t => { var (label, help) = LicenseTerms.Describe(table, t); return Chip(label, help, tint); });

    static TextBlock Muted(string text, double size = 12)
    {
        var t = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
        t.Classes.Add("muted");
        return t;
    }

    static Border Tag(string text)
    {
        var t = new TextBlock { Text = text, FontSize = 11 };
        t.Classes.Add("muted");
        var b = new Border { Child = t, VerticalAlignment = VerticalAlignment.Center };
        b.Classes.Add("tag");
        return b;
    }

    /// <summary>The summary, grouped terms and notes. <paramref name="explain"/> lists each term with its explanation (the full page).</summary>
    static StackPanel Body(LicenseInfo? info, bool explain)
    {
        var body = new StackPanel { Spacing = 8 };
        if (info is null)
        {
            body.Children.Add(new TextBlock { Text = UnrecognizedSummary, TextWrapping = TextWrapping.Wrap });
            return body;
        }

        body.Children.Add(new TextBlock { Text = info.Summary, TextWrapping = TextWrapping.Wrap });

        if (explain)
        {
            foreach (var (title, tags, table, tint) in new (string, List<string>, IReadOnlyDictionary<string, (string, string)>, Color)[]
            {
                ("You can", info.Permissions, LicenseTerms.Permissions, Green),
                ("You must", info.Conditions, LicenseTerms.Conditions, Amber),
                ("You can't count on", info.Limitations, LicenseTerms.Limitations, Red),
            })
            {
                if (tags.Count == 0) continue;
                var head = new TextBlock { Text = title.ToUpperInvariant(), Margin = new Thickness(0, 10, 0, 2) };
                head.Classes.Add("section");
                body.Children.Add(head);
                foreach (var tag in tags)
                {
                    var (label, help) = LicenseTerms.Describe(table, tag);
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 3) };
                    row.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = new SolidColorBrush(tint), VerticalAlignment = VerticalAlignment.Center });
                    var text = new StackPanel { Spacing = 1 };
                    text.Children.Add(new TextBlock { Text = label, FontWeight = FontWeight.SemiBold });
                    if (help.Length > 0) text.Children.Add(Muted(help));
                    row.Children.Add(text);
                    body.Children.Add(row);
                }
            }
        }
        else
        {
            if (info.Permissions.Count > 0) body.Children.Add(Group("You can", Chips(info.Permissions, LicenseTerms.Permissions, Green)));
            if (info.Conditions.Count > 0) body.Children.Add(Group("You must", Chips(info.Conditions, LicenseTerms.Conditions, Amber)));
            if (info.Limitations.Count > 0) body.Children.Add(Group("You can't count on", Chips(info.Limitations, LicenseTerms.Limitations, Red)));
        }

        foreach (var note in info.Notes)
            body.Children.Add(Muted("•  " + note));
        return body;
    }

    static Control Header(LicenseInfo? info, double titleSize)
    {
        var icon = new ShapePath { Data = Geometry.Parse(IconLicense), Width = 22, Height = 22, Stretch = Stretch.None, StrokeThickness = 1.7, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round, VerticalAlignment = VerticalAlignment.Center };
        icon.Classes.Add("tile-icon");
        var tags = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (info is not null) { tags.Children.Add(Tag(info.Id)); if (info.Category.Length > 0) tags.Children.Add(Tag(info.Category)); }
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(new TextBlock { Text = info?.Name ?? "Unrecognized license", FontSize = titleSize, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        title.Children.Add(tags);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        row.Children.Add(icon);
        row.Children.Add(title);
        return row;
    }

    /// <summary>The banner shown above a license file.</summary>
    public static Control Banner(LicenseInfo? info, bool collapsed, Action<bool> setCollapsed, Action dismiss, Action<string> openUrl)
    {
        var grid = new Grid { ColumnDefinitions = new("*,Auto") };
        var header = Header(info, 15);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        if (info is { Url.Length: > 0 }) buttons.Children.Add(Form.Button("Learn more", () => openUrl(info.Url)) is { } learn ? Trim(learn) : new Border());
        if (info is not null) buttons.Children.Add(Trim(Form.Button(collapsed ? "Show details" : "Hide details", () => setCollapsed(!collapsed))));
        var close = new Border { Child = new ShapePath { Data = Geometry.Parse("M6,6 L18,18 M18,6 L6,18"), Width = 10, Height = 10 } };
        close.Classes.Add("iconbtn");
        ToolTip.SetTip(close, "Dismiss");
        close.Tapped += (_, _) => dismiss();
        buttons.Children.Add(close);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(header);
        grid.Children.Add(buttons);

        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(grid);
        if (!collapsed)
        {
            stack.Children.Add(Body(info, explain: false));
            if (info is not null) stack.Children.Add(Muted(Disclaimer, 11)); // an unrecognized license has no summary to disclaim
        }

        var card = new Border { Child = stack, Padding = new Thickness(18, 14, 14, 12) };
        card.Classes.Add("license-banner");
        return card;
    }

    static Border Trim(Border b) { b.Margin = new Thickness(0); return b; }

    /// <summary>The full page for one license (Browse licenses).</summary>
    public static Control Page(LicenseInfo info, Action<string> openUrl)
    {
        var page = new StackPanel { MaxWidth = 720, Margin = new Thickness(40, 30, 40, 60), HorizontalAlignment = HorizontalAlignment.Left, Spacing = 12 };
        page.Children.Add(Header(info, 24));
        page.Children.Add(Body(info, explain: true));
        if (info.Url.Length > 0)
        {
            var b = Form.Button("Read more about this license", () => openUrl(info.Url), primary: true);
            b.Margin = new Thickness(0, 12, 0, 0);
            page.Children.Add(b);
        }
        page.Children.Add(Muted(Disclaimer, 11.5));
        return new ScrollViewer { Content = page, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
    }
}
