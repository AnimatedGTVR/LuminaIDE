using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace LuminaIDE;

/// <summary>Colour and short label for a file type; drawn as a small rounded badge.</summary>
static class FileTypes
{
    // (label, colour). Colours are mid-tones that read on both dark and light themes.
    static readonly Dictionary<string, (string Label, string Color)> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".rs"] = ("RS", "#F0843C"), [".py"] = ("PY", "#5B9FE0"), [".js"] = ("JS", "#E8C63E"), [".mjs"] = ("JS", "#E8C63E"), [".cjs"] = ("JS", "#E8C63E"),
        [".jsx"] = ("JX", "#E8C63E"), [".ts"] = ("TS", "#3B8FE8"), [".tsx"] = ("TX", "#3B8FE8"), [".json"] = ("{}", "#E9B93A"), [".jsonc"] = ("{}", "#E9B93A"),
        [".md"] = ("MD", "#7C93C9"), [".markdown"] = ("MD", "#7C93C9"), [".html"] = ("<>", "#F4623A"), [".htm"] = ("<>", "#F4623A"),
        [".css"] = ("#", "#4A9DF0"), [".scss"] = ("S", "#E0629B"), [".sass"] = ("S", "#E0629B"), [".less"] = ("L", "#4A6FD6"),
        [".vue"] = ("V", "#41B883"), [".svelte"] = ("S", "#FF5A2C"), [".astro"] = ("A", "#FF5D01"),
        [".c"] = ("C", "#5A9BE0"), [".h"] = ("H", "#8E7CE0"), [".cpp"] = ("C+", "#4F86D6"), [".cc"] = ("C+", "#4F86D6"), [".hpp"] = ("H+", "#8E7CE0"),
        [".cs"] = ("C#", "#A26BE0"), [".fs"] = ("F#", "#3FB0C7"), [".vb"] = ("VB", "#8B6BD6"),
        [".go"] = ("GO", "#00B0D8"), [".java"] = ("JV", "#E76F51"), [".kt"] = ("KT", "#A97BFF"), [".swift"] = ("SW", "#FF7A45"), [".scala"] = ("SC", "#E0503F"),
        [".rb"] = ("RB", "#E0475B"), [".php"] = ("PH", "#8892BF"), [".lua"] = ("LU", "#4D7DFF"), [".dart"] = ("DT", "#3CC1F5"), [".zig"] = ("ZG", "#F7A41D"),
        [".sh"] = ("SH", "#7CC95A"), [".bash"] = ("SH", "#7CC95A"), [".zsh"] = ("SH", "#7CC95A"), [".ps1"] = ("PS", "#4A8FD6"), [".bat"] = ("BT", "#7CC95A"),
        [".nix"] = ("NX", "#7EBAE4"), [".toml"] = ("TM", "#B08968"), [".yaml"] = ("YM", "#D96A6A"), [".yml"] = ("YM", "#D96A6A"), [".ini"] = ("IN", "#9AA5B8"),
        [".xml"] = ("XM", "#E67E22"), [".svg"] = ("SV", "#F2A03D"), [".sql"] = ("SQ", "#E8A33D"), [".graphql"] = ("GQ", "#E10098"), [".proto"] = ("PB", "#5B9FE0"),
        [".hs"] = ("HS", "#A074C4"), [".ml"] = ("ML", "#EE7F2D"), [".ex"] = ("EX", "#9D6BC4"), [".exs"] = ("EX", "#9D6BC4"), [".erl"] = ("ER", "#C0505A"),
        [".clj"] = ("CJ", "#7AC26B"), [".lisp"] = ("LP", "#8E9BD6"), [".vanta"] = ("V", "#FEA4E8"), [".gd"] = ("GD", "#5B9FE0"), [".tscn"] = ("TS", "#5B9FE0"),
        [".glsl"] = ("GL", "#5B9FE0"), [".hlsl"] = ("HL", "#5B9FE0"), [".wgsl"] = ("WG", "#5B9FE0"), [".tf"] = ("TF", "#8B6BE0"), [".sol"] = ("SL", "#8E9BD6"),
        [".lock"] = ("LK", "#8A93A6"), [".txt"] = ("TX", "#8A93A6"), [".log"] = ("LG", "#8A93A6"), [".env"] = ("EN", "#E0C24A"), [".diff"] = ("DF", "#9AA5B8"), [".patch"] = ("DF", "#9AA5B8"),
        [".png"] = ("IMG", "#3CB9A4"), [".jpg"] = ("IMG", "#3CB9A4"), [".jpeg"] = ("IMG", "#3CB9A4"), [".gif"] = ("IMG", "#3CB9A4"), [".webp"] = ("IMG", "#3CB9A4"), [".ico"] = ("IMG", "#3CB9A4"),
        [".zip"] = ("ZIP", "#B8A06A"), [".tar"] = ("TAR", "#B8A06A"), [".gz"] = ("GZ", "#B8A06A"),
    };

    static readonly Dictionary<string, (string Label, string Color)> ByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["package.json"] = ("NP", "#E5484D"), ["package-lock.json"] = ("NP", "#E5484D"), ["pnpm-lock.yaml"] = ("PN", "#F7B93E"), ["yarn.lock"] = ("YN", "#2C8EBB"),
        ["Dockerfile"] = ("DK", "#2496ED"), ["docker-compose.yml"] = ("DK", "#2496ED"), ["Makefile"] = ("MK", "#8AA36B"), ["CMakeLists.txt"] = ("CM", "#5B9FE0"),
        [".gitignore"] = ("GI", "#F05133"), [".gitattributes"] = ("GI", "#F05133"), ["LICENSE"] = ("LC", "#D6B25E"), ["NOTICE"] = ("LC", "#D6B25E"),
        ["Cargo.toml"] = ("CG", "#F0843C"), ["Cargo.lock"] = ("CG", "#B5651D"), ["README.md"] = ("MD", "#7C93C9"), ["CHANGELOG.md"] = ("CL", "#7CC95A"),
        [".editorconfig"] = ("EC", "#9AA5B8"), ["vite.config.ts"] = ("VT", "#A78BFA"), ["vite.config.js"] = ("VT", "#A78BFA"), ["tsconfig.json"] = ("TS", "#3B8FE8"),
    };

    public static (string Label, string Color)? For(string fileName)
    {
        if (ByName.TryGetValue(fileName, out var byName)) return byName;
        if (LicenseFiles.IsLicenseFile(fileName)) return ("LC", "#D6B25E"); // LICENSE, COPYING, UNLICENSE.txt, LICENSE-MIT...
        return ByExtension.TryGetValue(System.IO.Path.GetExtension(fileName), out var byExt) ? byExt : null;
    }
}

/// <summary>
/// A file or folder icon: a tinted folder glyph for folders (open or closed), a coloured type badge
/// for recognised files, and a neutral page for everything else. Folder colours follow the theme.
/// </summary>
public sealed class FileIcon : UserControl
{
    public static readonly StyledProperty<string?> FileNameProperty = AvaloniaProperty.Register<FileIcon, string?>(nameof(FileName));
    public static readonly StyledProperty<bool> IsDirProperty = AvaloniaProperty.Register<FileIcon, bool>(nameof(IsDir));
    public static readonly StyledProperty<bool> IsOpenProperty = AvaloniaProperty.Register<FileIcon, bool>(nameof(IsOpen));
    public static readonly StyledProperty<double> SizeProperty = AvaloniaProperty.Register<FileIcon, double>(nameof(Size), 16);

    public string? FileName { get => GetValue(FileNameProperty); set => SetValue(FileNameProperty, value); }
    public bool IsDir { get => GetValue(IsDirProperty); set => SetValue(IsDirProperty, value); }
    public bool IsOpen { get => GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }
    public double Size { get => GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    const string FolderClosed = "M1.8,4.6 A1.6,1.6 0 0 1 3.4,3 H6.5 L8.1,4.7 H12.6 A1.6,1.6 0 0 1 14.2,6.3 V11.4 A1.6,1.6 0 0 1 12.6,13 H3.4 A1.6,1.6 0 0 1 1.8,11.4 Z";
    const string FolderOpen = "M1.8,4.6 A1.6,1.6 0 0 1 3.4,3 H6.5 L8.1,4.7 H12.2 A1.5,1.5 0 0 1 13.7,6.2 V7 M3,13 H12.6 A1.5,1.5 0 0 0 14,11.9 L15.3,7.9 A1,1 0 0 0 14.3,7 H4.6 A1.5,1.5 0 0 0 3.2,8 L1.8,11.6";
    const string Page = "M4,2.4 H9.4 L12.4,5.4 V13.6 H4 Z M9.4,2.4 V5.4 H12.4";

    static FileIcon()
    {
        FileNameProperty.Changed.AddClassHandler<FileIcon>((x, _) => x.Rebuild());
        IsDirProperty.Changed.AddClassHandler<FileIcon>((x, _) => x.Rebuild());
        IsOpenProperty.Changed.AddClassHandler<FileIcon>((x, _) => x.Rebuild());
        SizeProperty.Changed.AddClassHandler<FileIcon>((x, _) => x.Rebuild());
    }

    public FileIcon()
    {
        IsHitTestVisible = false;
        VerticalAlignment = VerticalAlignment.Center;
        Rebuild();
    }

    void Rebuild()
    {
        double s = Size;
        Width = Height = s;

        if (IsDir)
        {
            var folder = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(IsOpen ? FolderOpen : FolderClosed), Stretch = Stretch.Uniform, Width = s, Height = s, StrokeThickness = 1.2, StrokeJoin = PenLineJoin.Round, StrokeLineCap = PenLineCap.Round };
            folder.Classes.Add(IsOpen ? "folder-open" : "folder");
            Content = folder;
            return;
        }

        if (FileTypes.For(FileName ?? "") is { } t)
        {
            var color = Color.Parse(t.Color);
            Content = new Border
            {
                Width = s, Height = s, CornerRadius = new CornerRadius(s * 0.28),
                Background = new SolidColorBrush(color, 0.20), BorderBrush = new SolidColorBrush(color, 0.45), BorderThickness = new Thickness(0.8),
                Child = new TextBlock
                {
                    Text = t.Label, Foreground = new SolidColorBrush(color), FontWeight = FontWeight.Bold,
                    FontSize = t.Label.Length >= 3 ? s * 0.39 : s * 0.50, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    LetterSpacing = -0.2,
                },
            };
            return;
        }

        var page = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(Page), Stretch = Stretch.Uniform, Width = s, Height = s, StrokeThickness = 1.1, StrokeJoin = PenLineJoin.Round };
        page.Classes.Add("page");
        Content = page;
    }
}
