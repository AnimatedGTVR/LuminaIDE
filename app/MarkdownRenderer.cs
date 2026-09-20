using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace LuminaIDE;

/// <summary>
/// Draws the block tree produced by the Rust Markdown parser (core/src/markdown.rs) with native
/// controls, coloured from the current theme. Remote images are never fetched; local ones are.
/// </summary>
sealed class MarkdownRenderer
{
    const string Mono = "avares://LuminaIDE/Assets/Fonts#JetBrains Mono,Cascadia Code,Fira Code,DejaVu Sans Mono,Noto Sans Mono,monospace";
    static readonly double[] HeadingSizes = [30, 24, 19, 16, 15, 14];
    static readonly Regex HtmlTag = new("<[^>]+>", RegexOptions.Compiled);
    static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["rs"] = "rust", ["cs"] = "csharp", ["c#"] = "csharp", ["c++"] = "cpp", ["cc"] = "cpp", ["js"] = "javascript",
        ["jsx"] = "javascript", ["ts"] = "typescript", ["tsx"] = "typescript", ["py"] = "python", ["sh"] = "shell",
        ["bash"] = "shell", ["zsh"] = "shell", ["yml"] = "yaml", ["kt"] = "kotlin",
    };

    readonly string? _baseDir;
    readonly string? _root;
    readonly bool _allowRemote;
    readonly bool _animateGifs;
    readonly ExtensionHost _ext;
    readonly Action<string> _openFile;
    readonly Action<string> _notify;
    readonly Theme _theme = ThemeManager.Current;
    readonly IBrush _fg, _muted, _accent, _border, _codeBg;
    readonly double _size = 14.5;

    /// <param name="baseDir">Folder of the Markdown file (relative images and links resolve against it).</param>
    /// <param name="root">The open folder, for root-relative "/images/x.png" paths.</param>
    /// <param name="allowRemote">Whether https:// images may be downloaded.</param>
    public MarkdownRenderer(string? baseDir, ExtensionHost ext, Action<string> openFile, Action<string> notify, string? root = null, bool allowRemote = true, bool animateGifs = true)
    {
        _baseDir = baseDir; _root = MarkdownImages.FindProjectRoot(baseDir, root); _allowRemote = allowRemote; _animateGifs = animateGifs; _ext = ext; _openFile = openFile; _notify = notify;
        IBrush B(string key) => new SolidColorBrush(_theme.UiColor(key));
        _fg = B("foreground"); _muted = B("muted"); _accent = B("accent"); _border = B("border"); _codeBg = B("input");
    }

    /// <summary>The rendered document, or a short message when the parser gave nothing back.</summary>
    public Control Render(string markdown)
    {
        var panel = new StackPanel { Spacing = 14, Margin = new Thickness(32, 24, 32, 48), MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Left };
        using var doc = Core.Markdown(markdown);
        if (doc is null) { panel.Children.Add(Note("Could not render this document.")); return panel; }
        foreach (var block in doc.RootElement.EnumerateArray()) panel.Children.Add(Block(block));
        if (panel.Children.Count == 0) panel.Children.Add(Note("Nothing to preview yet."));
        return panel;
    }

    // ------------------------------------------------------------- blocks --

    Control Block(JsonElement b)
    {
        switch (b.GetProperty("t").GetString())
        {
            case "h": return Heading(b);
            case "p": return Paragraph(b.GetProperty("c"), _size);
            case "quote": return Quote(b);
            case "list": return List(b);
            case "code": return Code(b.GetProperty("lang").GetString() ?? "", b.GetProperty("text").GetString() ?? "");
            case "table": return Table(b);
            case "hr": return new Border { Height = 1, Background = _border, Margin = new Thickness(0, 6) };
            case "html": return Html(b);
            default: return new Border();
        }
    }

    /// <summary>Raw HTML: its images are shown (READMEs often centre a logo this way); the rest is text with the tags removed.</summary>
    Control Html(JsonElement b)
    {
        var raw = b.GetProperty("text").GetString() ?? "";
        var text = WebUtility.HtmlDecode(HtmlTag.Replace(raw, "")).Trim();
        var images = b.TryGetProperty("imgs", out var arr) && arr.ValueKind == JsonValueKind.Array ? arr.EnumerateArray().ToArray() : [];
        if (images.Length == 0) return text.Length == 0 ? new Border() : Note(text);

        bool centered = raw.Contains("align=\"center\"", StringComparison.OrdinalIgnoreCase) || raw.Contains("<center", StringComparison.OrdinalIgnoreCase);
        var row = new WrapPanel { HorizontalAlignment = centered ? HorizontalAlignment.Center : HorizontalAlignment.Left };
        foreach (var img in images)
        {
            var host = MakeImage(img);
            host.Margin = new Thickness(0, 0, 8, 8);
            row.Children.Add(host);
        }
        if (text.Length == 0) return row;
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(row);
        var note = Note(text);
        if (centered) note.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(note);
        return stack;
    }

    ImageHost MakeImage(JsonElement r)
    {
        var src = r.GetProperty("src").GetString() ?? "";
        var alt = r.TryGetProperty("alt", out var a) ? a.GetString() ?? "" : "";
        double? width = r.TryGetProperty("w", out var w) && w.ValueKind == JsonValueKind.Number ? w.GetDouble() : null;
        double? height = r.TryGetProperty("h", out var hh) && hh.ValueKind == JsonValueKind.Number ? hh.GetDouble() : null;
        var href = r.TryGetProperty("href", out var h) ? h.GetString() : null;
        return new ImageHost(MarkdownImages.Classify(src, _baseDir, _root, _allowRemote), alt, width, height, href, OpenLink, _muted, _animateGifs);
    }

    Control Heading(JsonElement b)
    {
        int level = Math.Clamp(b.GetProperty("l").GetInt32(), 1, 6);
        var tb = Text(b.GetProperty("c"), HeadingSizes[level - 1], FontWeight.Bold);
        tb.Margin = new Thickness(0, level <= 2 ? 10 : 6, 0, 0);
        if (level > 2) return tb;
        return new Border { Child = tb, BorderBrush = _border, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 0, 6) };
    }

    Control Paragraph(JsonElement runs, double size)
    {
        // A paragraph made only of images (a line of badges, a screenshot) is laid out as a wrapping row.
        var arr = runs.EnumerateArray().ToArray();
        bool onlyImages = arr.Length > 0 && arr.Any(r => r.GetProperty("t").GetString() == "img") &&
                          arr.All(r => r.GetProperty("t").GetString() == "img" || (r.GetProperty("t").GetString() == "text" && string.IsNullOrWhiteSpace(r.GetProperty("s").GetString())) || r.GetProperty("t").GetString() == "br");
        if (onlyImages)
        {
            var row = new WrapPanel();
            foreach (var r in arr.Where(r => r.GetProperty("t").GetString() == "img"))
            {
                var host = MakeImage(r);
                host.Margin = new Thickness(0, 0, 8, 8);
                row.Children.Add(host);
            }
            return row;
        }
        return Text(runs, size, FontWeight.Normal);
    }

    Control Quote(JsonElement b)
    {
        var inner = new StackPanel { Spacing = 8 };
        foreach (var c in b.GetProperty("c").EnumerateArray()) inner.Children.Add(Block(c));
        return new Border
        {
            Child = inner, BorderBrush = _accent, BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(14, 2, 4, 2), Opacity = 0.85,
        };
    }

    Control List(JsonElement b)
    {
        bool ordered = b.GetProperty("ord").GetBoolean();
        int n = b.GetProperty("start").GetInt32();
        var list = new StackPanel { Spacing = 5 };
        foreach (var item in b.GetProperty("items").EnumerateArray())
        {
            string marker = ordered ? $"{n++}." : "•";
            var task = item.GetProperty("task");
            if (task.ValueKind is JsonValueKind.True or JsonValueKind.False) marker = task.GetBoolean() ? "☑" : "☐";

            var body = new StackPanel { Spacing = 5 };
            foreach (var c in item.GetProperty("c").EnumerateArray()) body.Children.Add(Block(c));

            var row = new Grid { ColumnDefinitions = new("Auto,*") };
            var m = new TextBlock { Text = marker, Foreground = _muted, FontSize = _size, MinWidth = 22, TextAlignment = TextAlignment.Right, Margin = new Thickness(0, 0, 8, 0) };
            Grid.SetColumn(body, 1);
            row.Children.Add(m);
            row.Children.Add(body);
            list.Children.Add(row);
        }
        return new Border { Child = list, Margin = new Thickness(6, 0, 0, 0) };
    }

    Control Code(string lang, string text)
    {
        var tb = new SelectableTextBlock { FontFamily = new FontFamily(Mono), FontSize = 13, Foreground = _fg, TextWrapping = TextWrapping.NoWrap };
        AddHighlighted(tb, lang, text);
        // Padding goes on the Border: a ScrollViewer draws its own Padding but leaves it out of
        // its measured height, which used to clip the last lines of every block.
        return new Border
        {
            Background = _codeBg, BorderBrush = _border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 12),
            Child = new ScrollViewer { Content = tb, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled },
        };
    }

    Control Table(JsonElement b)
    {
        var head = b.GetProperty("head").EnumerateArray().ToArray();
        var aligns = b.GetProperty("al").EnumerateArray().Select(a => a.GetString()).ToArray();
        int cols = Math.Max(1, head.Length);
        var grid = new Grid();
        for (int c = 0; c < cols; c++) grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

        int row = 0;
        void AddRow(JsonElement[] cells, bool header)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (int c = 0; c < cols; c++)
            {
                var tb = c < cells.Length ? Text(cells[c], 13.5, header ? FontWeight.Bold : FontWeight.Normal) : new TextBlock();
                if (tb is TextBlock t)
                    t.TextAlignment = c < aligns.Length ? aligns[c] switch { "c" => TextAlignment.Center, "r" => TextAlignment.Right, _ => TextAlignment.Left } : TextAlignment.Left;
                var cell = new Border
                {
                    Child = tb, Padding = new Thickness(10, 6), BorderBrush = _border, BorderThickness = new Thickness(0, 0, 1, 1),
                    Background = header ? _codeBg : Brushes.Transparent,
                };
                Grid.SetRow(cell, row); Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
            row++;
        }

        AddRow(head, header: true);
        foreach (var r in b.GetProperty("rows").EnumerateArray()) AddRow(r.EnumerateArray().ToArray(), header: false);
        return new Border { Child = grid, BorderBrush = _border, BorderThickness = new Thickness(1, 1, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 320 };
    }

    // ------------------------------------------------------------ inlines --

    TextBlock Note(string text) => new() { Text = text, Foreground = _muted, TextWrapping = TextWrapping.Wrap, FontSize = 13 };

    SelectableTextBlock Text(JsonElement runs, double size, FontWeight weight)
    {
        var tb = new SelectableTextBlock { FontSize = size, FontWeight = weight, Foreground = _fg, TextWrapping = TextWrapping.Wrap };
        bool hasImage = runs.EnumerateArray().Any(r => r.GetProperty("t").GetString() == "img");
        // A fixed line height would clip pictures placed inline in the text, so only text-only paragraphs get it.
        if (!hasImage) tb.LineHeight = size * 1.55;
        foreach (var r in runs.EnumerateArray()) AddRun(tb, r, weight);
        return tb;
    }

    void AddRun(SelectableTextBlock tb, JsonElement r, FontWeight baseWeight)
    {
        switch (r.GetProperty("t").GetString())
        {
            case "br":
                tb.Inlines!.Add(new LineBreak());
                return;
            case "img":
                tb.Inlines!.Add(new InlineUIContainer(MakeImage(r)));
                return;
        }

        var text = r.GetProperty("s").GetString() ?? "";
        bool bold = Flag(r, "b"), italic = Flag(r, "i"), strike = Flag(r, "x"), code = Flag(r, "code");
        var weight = bold ? FontWeight.Bold : baseWeight;

        if (r.TryGetProperty("href", out var href) && href.GetString() is { } url)
        {
            var link = new TextBlock
            {
                Text = text, Foreground = _accent, Cursor = new Cursor(StandardCursorType.Hand), FontWeight = weight,
                FontStyle = italic ? FontStyle.Italic : FontStyle.Normal, FontFamily = code ? new FontFamily(Mono) : FontFamily.Default,
                TextDecorations = TextDecorations.Underline,
            };
            ToolTip.SetTip(link, url);
            link.Tapped += (_, _) => OpenLink(url);
            tb.Inlines!.Add(new InlineUIContainer(link));
            return;
        }

        var run = new Run(text) { FontWeight = weight, FontStyle = italic ? FontStyle.Italic : FontStyle.Normal };
        if (strike) run.TextDecorations = TextDecorations.Strikethrough;
        if (code)
        {
            run.FontFamily = new FontFamily(Mono);
            run.Background = _codeBg;
            run.Foreground = new SolidColorBrush(Color.Parse(_theme.Syntax.GetValueOrDefault("constant") ?? "#e8a06a"));
        }
        tb.Inlines!.Add(run);
    }

    static bool Flag(JsonElement r, string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>Colours a fenced block with the same tokenizer the editor uses.</summary>
    void AddHighlighted(SelectableTextBlock tb, string lang, string text)
    {
        var id = ResolveLanguage(lang);
        var spans = id is null ? [] : Core.Tokenize(id, text);
        var brushes = ThemeManager.SyntaxBrushes;
        int pos = 0;
        for (int i = 0; i + 2 < spans.Length; i += 3)
        {
            int start = Math.Clamp(spans[i], pos, text.Length), end = Math.Clamp(spans[i] + spans[i + 1], start, text.Length);
            int kind = spans[i + 2];
            if (start > pos) tb.Inlines!.Add(new Run(text[pos..start]));
            if (end > start) tb.Inlines!.Add(new Run(text[start..end]) { Foreground = kind >= 0 && kind < brushes.Length ? brushes[kind] : null });
            pos = end;
        }
        if (pos < text.Length) tb.Inlines!.Add(new Run(text[pos..]));
    }

    string? ResolveLanguage(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        if (Aliases.TryGetValue(tag, out var alias)) tag = alias;
        return _ext.Languages.FirstOrDefault(l => l.Id.Equals(tag, StringComparison.OrdinalIgnoreCase) || l.Name.Equals(tag, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    // ------------------------------------------------------ images & links --

    void OpenLink(string href)
    {
        if (href.StartsWith('#')) return;
        if (Uri.TryCreate(href, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme is "http" or "https" or "mailto") Platform.Open(uri.AbsoluteUri);
            return; // other schemes (file:, javascript:, ...) are ignored on purpose
        }

        if (_baseDir is null) return;
        var target = Path.GetFullPath(Path.Combine(_baseDir, Uri.UnescapeDataString(href.Split('#')[0])));
        if (File.Exists(target)) _openFile(target);
        else _notify($"{Path.GetFileName(target)} doesn't exist.");
    }
}
