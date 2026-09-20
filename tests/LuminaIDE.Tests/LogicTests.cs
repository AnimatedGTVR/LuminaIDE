using System.Text.Json;
using LuminaIDE;
using Xunit;

namespace LuminaIDE.Tests;

/// <summary>Points Settings at a throw-away config folder for the duration of a test.</summary>
public sealed class TempConfig : IDisposable
{
    readonly string? _old = Environment.GetEnvironmentVariable("LUMINA_CONFIG_HOME");
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "lumina-tests-" + Guid.NewGuid().ToString("N"));

    public TempConfig()
    {
        Directory.CreateDirectory(Root);
        Environment.SetEnvironmentVariable("LUMINA_CONFIG_HOME", Root);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("LUMINA_CONFIG_HOME", _old);
        try { Directory.Delete(Root, recursive: true); } catch { /* best effort */ }
    }
}

[Collection("uses environment variables")]
public class SettingsTests
{
    [Fact]
    public void Defaults_are_sane_and_normalize_clamps_wild_values()
    {
        var s = new Settings { EditorFontSize = 900, TabSize = 0, AutoSave = "sometimes", Agent = "gpt", MarkdownMode = "??" };
        s.Normalize();
        Assert.Equal(40, s.EditorFontSize);
        Assert.Equal(1, s.TabSize);
        Assert.Equal("off", s.AutoSave);
        Assert.Equal("claude", s.Agent);
        Assert.Equal("split", s.MarkdownMode);
    }

    [Fact]
    public void Config_folder_follows_explicit_override_on_every_platform()
    {
        using var cfg = new TempConfig();
        Assert.StartsWith(cfg.Root, Settings.ConfigDir);
        Assert.True(Path.IsPathRooted(Settings.ConfigDir));
    }

    [Fact]
    public void Relative_config_override_is_ignored()
    {
        using var cfg = new TempConfig();
        Environment.SetEnvironmentVariable("LUMINA_CONFIG_HOME", "relative-config");
        Assert.True(Path.IsPathFullyQualified(Settings.ConfigDir));
        Assert.DoesNotContain("relative-config", Settings.ConfigDir);
    }

    [Fact]
    public void Loads_comments_trailing_commas_and_any_key_casing()
    {
        using var cfg = new TempConfig();
        Directory.CreateDirectory(Settings.ConfigDir);
        File.WriteAllText(Settings.FilePath, """
            // my settings
            {
              "Theme": "Nord",          // old PascalCase keys still work
              "tabSize": 2,
              "wordWrap": true,
            }
            """);
        var s = Settings.Load();
        Assert.Null(Settings.LoadProblem);
        Assert.Equal("Nord", s.Theme);
        Assert.Equal(2, s.TabSize);
        Assert.True(s.WordWrap);
        Assert.Equal(14, s.EditorFontSize); // untouched keys keep their defaults
    }

    [Fact]
    public void A_broken_file_is_kept_not_overwritten_and_reported()
    {
        using var cfg = new TempConfig();
        Directory.CreateDirectory(Settings.ConfigDir);
        File.WriteAllText(Settings.FilePath, "{ \"theme\": ");
        var s = Settings.Load();
        Assert.NotNull(Settings.LoadProblem);
        Assert.Equal("Vanta Night", s.Theme);
        Assert.Equal("{ \"theme\": ", File.ReadAllText(Settings.FilePath + ".broken"));
    }

    [Fact]
    public void Save_then_load_round_trips_and_writes_a_header_comment()
    {
        using var cfg = new TempConfig();
        var s = new Settings { Theme = "Dracula", TabSize = 8, EditorFontFamily = "Fira Code" };
        s.AddRecent("/a"); s.AddRecent("/b"); s.AddRecent("/a");
        s.Save();

        var text = File.ReadAllText(Settings.FilePath);
        Assert.StartsWith("//", text);
        var back = Settings.Load();
        Assert.Equal("Dracula", back.Theme);
        Assert.Equal(8, back.TabSize);
        Assert.Equal(["/a", "/b"], back.RecentFolders);
    }
}

public class NodeProjectTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "lumina-node-" + Guid.NewGuid().ToString("N"));
    public NodeProjectTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    string Write(string rel, string content)
    {
        var p = Path.Combine(_root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content);
        return p;
    }

    [Fact]
    public void Reads_scripts_frameworks_and_orders_the_common_ones_first()
    {
        Write("package.json", """
            { "name": "site", "version": "2.0.0",
              "scripts": { "zeta": "x", "build": "vite build", "dev": "vite", "test": "vitest" },
              "dependencies": { "react": "1" }, "devDependencies": { "vite": "5", "typescript": "5" } }
            """);
        var p = NodeProjects.Detect(_root, null)!;
        Assert.Equal("site", p.Name);
        Assert.Equal(["dev", "build", "test", "zeta"], p.Scripts.Select(s => s.Name));
        Assert.True(p.HasVite);
        Assert.Contains("React", p.Badges);
        Assert.Contains("TypeScript", p.Badges);
        Assert.False(p.HasNodeModules);
    }

    [Theory]
    [InlineData("pnpm-lock.yaml", "pnpm", "pnpm run dev", "pnpm install")]
    [InlineData("yarn.lock", "yarn", "yarn dev", "yarn install")]
    [InlineData("bun.lockb", "bun", "bun run dev", "bun install")]
    [InlineData("package-lock.json", "npm", "npm run dev", "npm install")]
    public void Picks_the_package_manager_from_the_lockfile(string lockfile, string manager, string run, string install)
    {
        Write("package.json", """{ "name": "a", "scripts": { "dev": "x" } }""");
        Write(lockfile, "x");
        var p = NodeProjects.Detect(_root, null)!;
        Assert.Equal(manager, p.Manager);
        Assert.Equal(run, NodeProjects.RunCommand(p, "dev"));
        Assert.Equal(install, NodeProjects.InstallCommand(manager));
    }

    [Fact]
    public void The_packageManager_field_beats_lockfiles_and_no_lockfile_means_npm()
    {
        Write("package.json", """{ "name": "a", "packageManager": "pnpm@9.1.0" }""");
        Write("package-lock.json", "x");
        Assert.Equal("pnpm", NodeProjects.Detect(_root, null)!.Manager);

        File.WriteAllText(Path.Combine(_root, "package.json"), """{ "name": "a" }""");
        File.Delete(Path.Combine(_root, "package-lock.json"));
        Assert.Equal("npm", NodeProjects.Detect(_root, null)!.Manager);
    }

    [Fact]
    public void Walks_up_from_the_active_file_to_the_nearest_package_json_but_not_past_the_root()
    {
        Write("package.json", """{ "name": "outer" }""");
        Write("packages/web/package.json", """{ "name": "web" }""");
        Write("packages/web/src/deep/file.ts", "");
        Write("docs/readme.md", "");

        Assert.Equal("web", NodeProjects.Detect(_root, Path.Combine(_root, "packages/web/src/deep"))!.Name);
        Assert.Equal("outer", NodeProjects.Detect(_root, Path.Combine(_root, "docs"))!.Name);
        Assert.Null(NodeProjects.Detect(Path.Combine(_root, "docs"), Path.Combine(_root, "docs")));
    }

    [Fact]
    public void Invalid_package_json_is_treated_as_no_project()
    {
        Write("package.json", "{ not json");
        Assert.Null(NodeProjects.Detect(_root, null));
    }

    [Theory]
    [InlineData("dev", true)]
    [InlineData("build:prod", true)]
    [InlineData("test.unit", true)]
    [InlineData("deploy; rm -rf /", false)]
    [InlineData("$(whoami)", false)]
    [InlineData("a && b", false)]
    [InlineData("`x`", false)]
    [InlineData("-rf", false)]
    [InlineData("", false)]
    public void Only_plain_script_names_can_reach_the_shell(string name, bool safe) =>
        Assert.Equal(safe, NodeProjects.IsSafeScript(name));

    [Theory]
    [InlineData("my-site", true)]
    [InlineData("site.v2", true)]
    [InlineData("My Site", false)]
    [InlineData("../evil", false)]
    [InlineData("a;b", false)]
    [InlineData("", false)]
    public void Project_names_are_restricted_too(string name, bool safe) =>
        Assert.Equal(safe, NodeProjects.IsSafeProjectName(name));

    [Fact]
    public void Static_site_scaffold_writes_three_files_and_refuses_a_non_empty_folder()
    {
        var index = NodeProjects.ScaffoldStaticSite(_root, "hello-site");
        var dir = Path.GetDirectoryName(index)!;
        Assert.Equal(["index.html", "main.js", "style.css"], Directory.GetFiles(dir).Select(Path.GetFileName).Order());
        var html = File.ReadAllText(index);
        Assert.Contains("<title>hello-site</title>", html);
        Assert.DoesNotContain("{{", html);
        Assert.StartsWith("<!doctype html>", html);

        Assert.Throws<IOException>(() => NodeProjects.ScaffoldStaticSite(_root, "hello-site"));
    }
}

public class FuzzyTests
{
    [Fact]
    public void Subsequence_matching_ranks_word_starts_and_short_names_higher()
    {
        Assert.True(Fuzzy.Score("Quick Open", "qo") > 0);
        Assert.True(Fuzzy.Score("Quick Open", "qo") > Fuzzy.Score("Squeeze topic", "qo"));
        Assert.Equal(-1, Fuzzy.Score("Save", "sx"));
        Assert.True(Fuzzy.Score("main.rs", "main") > Fuzzy.Score("a/very/long/directory/name/with/main.rs", "main"));
        Assert.Equal(0, Fuzzy.Score("anything", ""));
    }

    [Fact]
    public void Matches_on_the_directory_too_but_prefers_the_file_name()
    {
        var byName = new PickItem("readme.md", "docs");
        var byDir = new PickItem("index.md", "readme");
        Assert.True(Fuzzy.Score(byName, "readme") > Fuzzy.Score(byDir, "readme"));
        Assert.True(Fuzzy.Score(byDir, "readme") >= 0);
    }
}

public class ThemeTests
{
    [Fact]
    public void Legacy_glass_theme_loads_as_opaque_with_fallback_colours()
    {
        var t = JsonSerializer.Deserialize<Theme>("""{ "name": "G", "type": "dark", "glass": true, "ui": { "background": "#80112233" } }""")!;
        var c = t.UiColor("background");
        Assert.Equal((0xff, 0x11, 0x22, 0x33), (c.A, c.R, c.G, c.B));
        Assert.Equal(t.UiFallback("accent"), t.UiColor("accent")); // missing keys come from Vanta Night
    }

    [Fact]
    public void Legacy_glass_setting_migrates_to_the_default_theme()
    {
        Assert.DoesNotContain("glass", JsonSerializer.Serialize(new Theme { Name = "x" }));
        var settings = new Settings { Theme = "Liquid Glass Night" };
        settings.Normalize();
        Assert.Equal("Vanta Night", settings.Theme);
    }

    [Fact]
    public void Every_syntax_kind_has_a_colour_even_in_a_partial_theme()
    {
        var brushes = new Theme { Name = "partial", Syntax = { ["keyword"] = "#ff0000" } }.SyntaxBrushes();
        Assert.Equal(SyntaxKinds.Names.Length, brushes.Length);
    }
}

public class PlatformTests
{
    [Fact]
    public void Quoting_neutralises_shell_metacharacters_on_unix()
    {
        if (Platform.IsWindows) return;
        Assert.Equal("'plain'", Platform.Quote("plain"));
        Assert.Equal("'it'\\''s'", Platform.Quote("it's"));
        Assert.Equal("'a b; rm -rf / $(x) `y`'", Platform.Quote("a b; rm -rf / $(x) `y`"));
    }

    [Fact]
    public void Mac_shortcut_labels_use_symbols_and_other_platforms_are_untouched()
    {
        var label = Platform.Keys("Ctrl+Shift+P");
        Assert.Equal(Platform.IsMac ? "⌘⇧P" : "Ctrl+Shift+P", label);
        Assert.Equal("", Platform.Keys(""));
    }

    [Fact]
    public void Which_finds_a_program_that_exists_and_not_one_that_does_not()
    {
        Assert.NotNull(Platform.Which(Platform.IsWindows ? "cmd" : "sh"));
        Assert.Null(Platform.Which("definitely-not-a-real-program-xyz"));
    }
}
