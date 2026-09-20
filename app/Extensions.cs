using System.Text.Json;
using System.Text.Json.Serialization;

namespace LuminaIDE;

/// <summary>Run is a shell template with {file}, {dir} and {name}; null when the language has no run command.</summary>
public sealed record LanguageInfo(string Id, string Name, string? Run = null);

public sealed class Manifest
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
    [JsonPropertyName("version")] public string Version { get; set; } = "0.0.0";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("languages")] public List<string> Languages { get; set; } = [];
    [JsonPropertyName("themes")] public List<string> Themes { get; set; } = [];
    [JsonPropertyName("licenses")] public List<string> Licenses { get; set; } = [];
}

public sealed record Extension(Manifest Manifest, string Dir, bool BuiltIn, int LanguageCount, int ThemeCount, int LicenseCount = 0)
{
    public string Title => Manifest.DisplayName ?? Manifest.Name;
}

/// <summary>
/// Discovers extensions (a folder with an extension.json) in the app's own
/// `extensions/` folder and in ~/.config/luminaide/extensions, then feeds their
/// languages to the Rust tokenizer and collects their themes.
/// </summary>
public sealed class ExtensionHost
{
    public static string UserDir => Path.Combine(Settings.ConfigDir, "extensions");
    static string BuiltInDir => Path.Combine(AppContext.BaseDirectory, "extensions");

    public List<Extension> Extensions { get; } = [];
    public List<LanguageInfo> Languages { get; } = [];
    public List<Theme> Themes { get; } = [];
    public List<LicenseInfo> Licenses { get; } = [];
    public List<string> Problems { get; } = [];
    /// <summary>Theme name -> file, for themes the user dropped into the themes folder (they can be edited in place).</summary>
    public Dictionary<string, string> UserThemeFiles { get; } = [];

    public void Load()
    {
        Extensions.Clear(); Languages.Clear(); Themes.Clear(); Licenses.Clear(); Problems.Clear(); UserThemeFiles.Clear();
        Core.ClearLanguages();
        Core.ClearLicenses();

        Directory.CreateDirectory(UserDir);
        foreach (var (root, builtIn) in new[] { (BuiltInDir, true), (UserDir, false) })
        {
            if (!Directory.Exists(root)) continue;
            foreach (var dir in Directory.GetDirectories(root).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                LoadOne(dir, builtIn);
        }
        LoadUserThemes();
        if (Themes.Count == 0) Themes.Add(Theme.Fallback);
    }

    /// <summary>Any *.json in ~/.config/luminaide/themes is a theme; no extension manifest needed.</summary>
    void LoadUserThemes()
    {
        try { Directory.CreateDirectory(Settings.ThemesDir); } catch { return; }
        foreach (var file in Directory.GetFiles(Settings.ThemesDir, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var t = Theme.Load(file);
            if (t is null || string.IsNullOrWhiteSpace(t.Name)) { Problems.Add($"themes/{Path.GetFileName(file)}: not a valid theme (needs a \"name\")"); continue; }
            Themes.RemoveAll(x => x.Name == t.Name);
            Themes.Add(t);
            UserThemeFiles[t.Name] = file;
        }
    }

    void LoadOne(string dir, bool builtIn)
    {
        var manifestPath = Path.Combine(dir, "extension.json");
        if (!File.Exists(manifestPath)) return;

        Manifest? m;
        try { m = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath)); }
        catch (Exception e) { Problems.Add($"{Path.GetFileName(dir)}: bad extension.json ({e.Message})"); return; }
        if (m is null || string.IsNullOrWhiteSpace(m.Name)) { Problems.Add($"{Path.GetFileName(dir)}: extension.json needs a \"name\""); return; }

        int langs = 0, themes = 0, licenses = 0;
        foreach (var rel in m.Languages)
        {
            try
            {
                var json = File.ReadAllText(Path.Combine(dir, rel));
                if (!Core.RegisterLanguage(json)) throw new InvalidDataException("invalid language definition");
                var info = JsonSerializer.Deserialize<JsonElement>(json);
                var id = info.GetProperty("id").GetString()!;
                var name = info.TryGetProperty("name", out var n) ? n.GetString() ?? id : id;
                Languages.RemoveAll(l => l.Id == id);
                Languages.Add(new(id, name, RunCommandFor(info)));
                langs++;
            }
            catch (Exception e) { Problems.Add($"{m.Name}/{rel}: {e.Message}"); }
        }
        foreach (var rel in m.Themes)
        {
            var t = Theme.Load(Path.Combine(dir, rel));
            if (t is null || string.IsNullOrWhiteSpace(t.Name)) { Problems.Add($"{m.Name}/{rel}: invalid theme"); continue; }
            Themes.RemoveAll(x => x.Name == t.Name);
            Themes.Add(t);
            themes++;
        }
        foreach (var rel in m.Licenses)
        {
            try
            {
                var json = File.ReadAllText(Path.Combine(dir, rel));
                var info = JsonSerializer.Deserialize<LicenseInfo>(json) ?? throw new InvalidDataException("empty license file");
                if (string.IsNullOrWhiteSpace(info.Id) || string.IsNullOrWhiteSpace(info.Name)) throw new InvalidDataException("a license needs an \"id\" and a \"name\"");
                if (!Core.RegisterLicense(json)) throw new InvalidDataException("a license needs at least one \"matchAll\" phrase");
                Licenses.RemoveAll(l => l.Id == info.Id);
                Licenses.Add(info);
                licenses++;
            }
            catch (Exception e) { Problems.Add($"{m.Name}/{rel}: {e.Message}"); }
        }
        Extensions.Add(new(m, dir, builtIn, langs, themes, licenses));
    }

    /// <summary>"runWindows" / "runMac" override "run" on that OS; an empty override turns running off there.</summary>
    static string? RunCommandFor(JsonElement info)
    {
        string? Get(string key) => info.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var run = (Platform.IsWindows ? Get("runWindows") : Platform.IsMac ? Get("runMac") : null) ?? Get("run");
        return string.IsNullOrWhiteSpace(run) ? null : run;
    }

    public string LanguageName(string? id) => Languages.FirstOrDefault(l => l.Id == id)?.Name ?? "Plain Text";
}
