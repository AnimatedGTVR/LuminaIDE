using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace LuminaIDE;

public sealed record NodeProject(
    string Dir, string Name, string Version, List<(string Name, string Command)> Scripts,
    string Manager, List<string> Badges, bool HasNodeModules, bool HasVite);

/// <summary>Finds and understands a package.json: scripts, package manager, frameworks.</summary>
static class NodeProjects
{
    static readonly Regex SafeScript = new(@"^[A-Za-z0-9][A-Za-z0-9:_.\-]*$", RegexOptions.Compiled);
    static readonly Regex SafeProjectName = new(@"^[a-z0-9][a-z0-9._-]{0,60}$", RegexOptions.Compiled);

    static readonly (string Dep, string Badge)[] Known =
    [
        ("vite", "Vite"), ("react", "React"), ("vue", "Vue"), ("svelte", "Svelte"), ("next", "Next.js"), ("nuxt", "Nuxt"),
        ("astro", "Astro"), ("@angular/core", "Angular"), ("solid-js", "Solid"), ("webpack", "webpack"), ("typescript", "TypeScript"),
        ("tailwindcss", "Tailwind"), ("electron", "Electron"), ("express", "Express"), ("eslint", "ESLint"),
    ];

    /// <summary>Script names go into a shell command line, so only plain names are runnable.</summary>
    public static bool IsSafeScript(string name) => SafeScript.IsMatch(name);
    public static bool IsSafeProjectName(string name) => SafeProjectName.IsMatch(name);

    /// <summary>The package.json nearest to the active file (walking up to the root), else the root's own.</summary>
    public static NodeProject? Detect(string? root, string? activeDir)
    {
        if (root is null) return null;
        var dir = activeDir is not null && activeDir.StartsWith(root) ? activeDir : root;
        while (true)
        {
            if (File.Exists(Path.Combine(dir, "package.json"))) return Load(dir, root);
            if (string.Equals(dir, root, StringComparison.Ordinal) || Path.GetDirectoryName(dir) is not { } parent || parent.Length < root.Length) return null;
            dir = parent;
        }
    }

    static NodeProject? Load(string dir, string root)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "package.json")), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var r = doc.RootElement;
            string Str(string key) => r.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

            var scripts = new List<(string, string)>();
            if (r.TryGetProperty("scripts", out var s) && s.ValueKind == JsonValueKind.Object)
                foreach (var p in s.EnumerateObject()) scripts.Add((p.Name, p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : ""));
            string[] order = ["dev", "start", "build", "preview", "test", "lint"];
            scripts = scripts.OrderBy(x => Array.IndexOf(order, x.Item1) is var i && i >= 0 ? i : 99).ThenBy(x => x.Item1, StringComparer.Ordinal).ToList();

            var deps = new HashSet<string>();
            foreach (var key in new[] { "dependencies", "devDependencies", "peerDependencies" })
                if (r.TryGetProperty(key, out var d) && d.ValueKind == JsonValueKind.Object)
                    foreach (var p in d.EnumerateObject()) deps.Add(p.Name);

            bool hasVite = deps.Contains("vite") || Directory.GetFiles(dir, "vite.config.*").Length > 0;
            var badges = Known.Where(k => deps.Contains(k.Dep)).Select(k => k.Badge).ToList();
            if (hasVite && !badges.Contains("Vite")) badges.Insert(0, "Vite");

            return new NodeProject(dir, Str("name") is { Length: > 0 } n ? n : Path.GetFileName(dir), Str("version"), scripts,
                Manager(dir, root, Str("packageManager")), badges, Directory.Exists(Path.Combine(dir, "node_modules")), hasVite);
        }
        catch { return null; } // unreadable or invalid package.json: treat as "no project"
    }

    /// <summary>From the "packageManager" field, else the nearest lockfile, else npm.</summary>
    static string Manager(string dir, string root, string field)
    {
        foreach (var m in new[] { "pnpm", "yarn", "bun", "npm" })
            if (field.StartsWith(m + "@")) return m;
        for (var d = dir; ; d = Path.GetDirectoryName(d)!)
        {
            if (File.Exists(Path.Combine(d, "pnpm-lock.yaml"))) return "pnpm";
            if (File.Exists(Path.Combine(d, "yarn.lock"))) return "yarn";
            if (File.Exists(Path.Combine(d, "bun.lockb")) || File.Exists(Path.Combine(d, "bun.lock"))) return "bun";
            if (File.Exists(Path.Combine(d, "package-lock.json"))) return "npm";
            if (d == root || Path.GetDirectoryName(d) is null) return "npm";
        }
    }

    public static string RunCommand(NodeProject p, string script) => p.Manager switch
    {
        "yarn" => $"yarn {script}",
        _ => $"{p.Manager} run {script}",
    };

    public static string InstallCommand(string manager) => manager == "yarn" ? "yarn install" : $"{manager} install";

    public static string ViteCreateCommand(string name, string template) => $"npm create vite@latest {name} -- --template {template}";

    /// <summary>Writes a small, dependency-free site (index.html, style.css, main.js) and returns index.html.</summary>
    public static string ScaffoldStaticSite(string parent, string name)
    {
        var dir = Path.Combine(parent, name);
        if (Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any()) throw new IOException($"{name} already exists and isn't empty.");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "index.html"), $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>{{name}}</title>
              <link rel="stylesheet" href="style.css">
            </head>
            <body>
              <main>
                <h1>{{name}}</h1>
                <p>Edit <code>index.html</code>, <code>style.css</code> and <code>main.js</code>, then press <kbd>F5</kbd> in LuminaIDE to open this page in your browser.</p>
                <button id="hello">Say hello</button>
                <p id="out" aria-live="polite"></p>
              </main>
              <script src="main.js"></script>
            </body>
            </html>
            """.Replace("\n            ", "\n").TrimStart() + "\n");
        File.WriteAllText(Path.Combine(dir, "style.css"), """
            :root { color-scheme: light dark; --accent: #7c6cf0; }
            * { box-sizing: border-box; }
            body {
              margin: 0; min-height: 100vh; display: grid; place-items: center;
              font: 16px/1.6 system-ui, sans-serif;
              background: radial-gradient(60rem 40rem at 20% 10%, color-mix(in srgb, var(--accent) 25%, transparent), transparent), Canvas;
            }
            main { max-width: 34rem; padding: 2rem; }
            h1 { font-size: 2.6rem; margin: 0 0 .5rem; }
            code, kbd { padding: .1em .4em; border-radius: .35em; background: color-mix(in srgb, currentColor 12%, transparent); }
            button {
              font: inherit; padding: .6em 1.1em; border: 0; border-radius: .7em;
              background: var(--accent); color: white; cursor: pointer;
            }
            button:hover { filter: brightness(1.1); }
            """.Replace("\n            ", "\n").TrimStart() + "\n");
        File.WriteAllText(Path.Combine(dir, "main.js"), """
            const out = document.querySelector('#out');
            document.querySelector('#hello').addEventListener('click', () => {
              out.textContent = `Hello! It is ${new Date().toLocaleTimeString()}.`;
            });
            """.Replace("\n            ", "\n").TrimStart() + "\n");
        return Path.Combine(dir, "index.html");
    }
}

/// <summary>Sidebar panel for web projects: package.json scripts, dev server link, project starters.</summary>
public sealed class NpmView : UserControl
{
    static readonly (string Label, string Template)[] ViteTemplates =
        [("Vanilla", "vanilla"), ("Vanilla TS", "vanilla-ts"), ("React", "react"), ("React TS", "react-ts"), ("Vue", "vue"), ("Vue TS", "vue-ts"), ("Svelte", "svelte"), ("Svelte TS", "svelte-ts")];

    readonly StackPanel _body = new() { Margin = new Thickness(12, 0, 12, 20), Spacing = 6 };
    string _newName = "my-site";
    string _template = "vanilla-ts";
    string? _devUrl;
    string? _message;
    bool _messageIsError;

    public Func<string?>? Root { get; set; }
    public Func<string?>? ActiveDir { get; set; }
    /// <summary>Runs a command line in the terminal panel, in the given folder.</summary>
    public Action<string, string>? RunInTerminal { get; set; }
    public Action<string>? OpenFile { get; set; }
    public Action? TreeChanged { get; set; }

    public NodeProject? Project { get; private set; }

    public NpmView()
    {
        var header = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(14, 14, 8, 6) };
        var title = new TextBlock { Text = "WEB & NPM", VerticalAlignment = VerticalAlignment.Center };
        title.Classes.Add("section");
        var refresh = new Border { Child = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M19.5,12 A7.5,7.5 0 1 1 17.3,6.7 M19.5,4.5 V8.5 H15.5") } };
        refresh.Classes.Add("iconbtn");
        ToolTip.SetTip(refresh, "Refresh");
        refresh.Tapped += (_, _) => Refresh();
        Grid.SetColumn(refresh, 1);
        header.Children.Add(title);
        header.Children.Add(refresh);

        var root = new Grid { RowDefinitions = new("Auto,*") };
        Grid.SetRow(header, 0);
        var scroll = new ScrollViewer { Content = _body, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1);
        root.Children.Add(header);
        root.Children.Add(scroll);
        Content = root;
    }

    public void SetDevUrl(string? url)
    {
        if (_devUrl == url) return;
        _devUrl = url;
        Refresh();
    }

    void Note(string text, bool error = false) { _message = text; _messageIsError = error; Refresh(); }

    static TextBlock Muted(string text, double size = 12)
    {
        var t = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
        t.Classes.Add("muted");
        return t;
    }

    static Control TagChip(string text)
    {
        var t = new TextBlock { Text = text, FontSize = 10.5 };
        t.Classes.Add("muted");
        var b = new Border { Child = t, Margin = new Thickness(0, 0, 5, 5), VerticalAlignment = VerticalAlignment.Center };
        b.Classes.Add("tag");
        return b;
    }

    /// <summary>Rebuilds the panel from what is on disk right now.</summary>
    public void Refresh()
    {
        _body.Children.Clear();
        var root = Root?.Invoke();
        if (root is null)
        {
            _body.Children.Add(Muted("Open a folder to see its package.json, npm scripts and dev server."));
            return;
        }

        Project = NodeProjects.Detect(root, ActiveDir?.Invoke());
        if (_message is not null)
        {
            var m = Muted(_message);
            if (_messageIsError) { m.Classes.Remove("muted"); m.Classes.Add("error"); }
            _body.Children.Add(m);
        }
        if (Platform.Which("node") is null)
            _body.Children.Add(Muted("Node.js was not found on your PATH. Install it from nodejs.org to run these scripts.") is { } warn ? Warn(warn) : new Border());

        if (_devUrl is not null) _body.Children.Add(DevServerCard(_devUrl));

        if (Project is { } p) ProjectSection(p);
        else _body.Children.Add(Muted("No package.json in this folder (or above the open file)."));

        CreateSection(root);
    }

    static Control Warn(TextBlock t) { t.Classes.Remove("muted"); t.Classes.Add("error"); return t; }

    Control DevServerCard(string url)
    {
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(new TextBlock { Text = "Dev server running", FontWeight = FontWeight.SemiBold });
        stack.Children.Add(Muted(url));
        var open = Form.Button("Open in browser", () => Platform.Open(url), primary: true);
        open.Margin = new Thickness(0, 4, 0, 0);
        stack.Children.Add(open);
        var card = new Border { Child = stack };
        card.Classes.Add("card");
        return card;
    }

    void ProjectSection(NodeProject p)
    {
        var head = new StackPanel { Spacing = 2, Margin = new Thickness(0, 4, 0, 4) };
        head.Children.Add(new TextBlock { Text = p.Name, FontSize = 15, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        var where = Platform.Tilde(p.Dir);
        head.Children.Add(Muted((p.Version.Length > 0 ? $"v{p.Version} · " : "") + where, 11.5));
        _body.Children.Add(head);

        var badges = new WrapPanel();
        badges.Children.Add(TagChip(p.Manager));
        foreach (var b in p.Badges) badges.Children.Add(TagChip(b));
        _body.Children.Add(badges);

        if (!p.HasNodeModules)
        {
            _body.Children.Add(Muted("Dependencies aren't installed yet (no node_modules)."));
            _body.Children.Add(Form.Button($"{NodeProjects.InstallCommand(p.Manager)}", () => RunInTerminal?.Invoke(p.Dir, NodeProjects.InstallCommand(p.Manager)), primary: true));
        }
        else _body.Children.Add(Form.Button("Install dependencies", () => RunInTerminal?.Invoke(p.Dir, NodeProjects.InstallCommand(p.Manager))));

        var label = new TextBlock { Text = "SCRIPTS", Margin = new Thickness(0, 10, 0, 2) };
        label.Classes.Add("section");
        _body.Children.Add(label);
        if (p.Scripts.Count == 0) _body.Children.Add(Muted("This package.json has no scripts."));
        foreach (var (name, command) in p.Scripts) _body.Children.Add(ScriptRow(p, name, command));
    }

    Control ScriptRow(NodeProject p, string name, string command)
    {
        bool safe = NodeProjects.IsSafeScript(name);
        var play = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M6,4 L19,12 L6,20 Z"), Width = 11, Height = 11, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center,
            Fill = safe ? (IBrush)Application.Current!.FindResource("Accent")! : Brushes.Gray, Margin = new Thickness(0, 0, 10, 0),
        };
        var text = new StackPanel { Spacing = 1 };
        text.Children.Add(new TextBlock { Text = name, FontWeight = FontWeight.SemiBold, FontSize = 13 });
        var cmd = Muted(command, 11);
        cmd.TextTrimming = TextTrimming.CharacterEllipsis;
        cmd.TextWrapping = TextWrapping.NoWrap;
        text.Children.Add(cmd);

        var grid = new Grid { ColumnDefinitions = new("Auto,*") };
        grid.Children.Add(play);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var row = new Border { Child = grid, Padding = new Thickness(8, 6) };
        row.Classes.Add("item");
        ToolTip.SetTip(row, safe ? $"Run {NodeProjects.RunCommand(p, name)}" : "This script name contains unusual characters, so it can't be started from here.");
        if (safe) row.Tapped += (_, _) => RunInTerminal?.Invoke(p.Dir, NodeProjects.RunCommand(p, name));
        else row.Opacity = 0.5;
        return row;
    }

    void CreateSection(string root)
    {
        var label = new TextBlock { Text = "NEW WEB PROJECT", Margin = new Thickness(0, 14, 0, 2) };
        label.Classes.Add("section");
        _body.Children.Add(label);

        var name = new TextBox { Text = _newName, Watermark = "project-name" };
        name.TextChanged += (_, _) => _newName = name.Text?.Trim() ?? "";
        _body.Children.Add(name);

        var chips = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (var (lab, tpl) in ViteTemplates)
        {
            var chip = Form.Chip(lab, tpl == _template, () => { _template = tpl; Refresh(); });
            chips.Children.Add(chip);
        }
        _body.Children.Add(chips);

        var buttons = new WrapPanel();
        buttons.Children.Add(Form.Button("Create Vite app", () =>
        {
            if (!NodeProjects.IsSafeProjectName(_newName)) { Note("Use lowercase letters, digits, dots, dashes or underscores for the name.", true); return; }
            _message = null;
            RunInTerminal?.Invoke(root, NodeProjects.ViteCreateCommand(_newName, _template));
            Note("Vite is scaffolding in the terminal. When it finishes, open the new folder, then press Refresh.");
        }, primary: true));
        buttons.Children.Add(Form.Button("New static website", () =>
        {
            if (!NodeProjects.IsSafeProjectName(_newName)) { Note("Use lowercase letters, digits, dots, dashes or underscores for the name.", true); return; }
            try
            {
                var index = NodeProjects.ScaffoldStaticSite(root, _newName);
                _message = null;
                TreeChanged?.Invoke();
                OpenFile?.Invoke(index);
                Note($"Created {_newName}/. Press F5 on index.html to open it in your browser.");
            }
            catch (Exception e) { Note(e.Message, true); }
        }));
        _body.Children.Add(buttons);
        _body.Children.Add(Muted("Vite needs Node.js. The static website needs nothing — just a browser.", 11));
    }
}
