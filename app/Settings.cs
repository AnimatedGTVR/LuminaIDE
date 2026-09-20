using System.Text.Json;

namespace LuminaIDE;

/// <summary>
/// Per-user settings in <c>settings.json</c> (see docs/CONFIG.md). The file may contain
/// // comments and trailing commas; keys are case-insensitive. Missing keys use the defaults below.
/// </summary>
public sealed class Settings
{
    // ---- appearance ----
    public string Theme { get; set; } = "Vanta Night";

    // ---- editor ----
    /// <summary>Empty = the built-in list (JetBrains Mono, Cascadia Code, Fira Code, DejaVu Sans Mono...).</summary>
    public string EditorFontFamily { get; set; } = "";
    public double EditorFontSize { get; set; } = 14;
    public int TabSize { get; set; } = 4;
    public bool InsertSpaces { get; set; } = true;
    public bool WordWrap { get; set; }
    public bool LineNumbers { get; set; } = true;
    public bool HighlightCurrentLine { get; set; } = true;
    /// <summary>"off", "afterDelay" (1.5 s after you stop typing) or "onFocusLost".</summary>
    public string AutoSave { get; set; } = "off";
    public bool TrimTrailingWhitespace { get; set; }
    public bool InsertFinalNewline { get; set; }

    // ---- terminal ----
    public double TerminalFontSize { get; set; } = 12.5;

    // ---- markdown ----
    /// <summary>How Markdown files open: "split" (editor + preview), "preview" or "editor".</summary>
    public string MarkdownMode { get; set; } = "split";
    /// <summary>Show images that live on the web (https://) in Markdown previews. Off = none are downloaded.</summary>
    public bool MarkdownRemoteImages { get; set; } = true;
    /// <summary>Play animated GIFs in Markdown previews (click one to pause it). Off = the first frame only.</summary>
    public bool MarkdownAnimateGifs { get; set; } = true;

    // ---- agent ----
    /// <summary>"claude" or "codex".</summary>
    public string Agent { get; set; } = "claude";
    /// <summary>"read" (plan / read-only) or "edit".</summary>
    public string AgentMode { get; set; } = "read";

    // ---- state ----
    /// <summary>The version whose release notes were last shown.</summary>
    public string LastVersion { get; set; } = "";
    public List<string> RecentFolders { get; set; } = [];

    // ------------------------------------------------------------------ files --

    static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// ~/.config/luminaide (or $XDG_CONFIG_HOME/luminaide; %APPDATA%\luminaide on Windows). DoNotVerify matters:
    /// by default .NET returns "" on Linux when the folder doesn't exist yet, which would silently
    /// become a relative path in the current directory.
    /// </summary>
    public static string ConfigDir
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify);
            if (string.IsNullOrEmpty(root)) root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            return Path.Combine(root, "luminaide");
        }
    }

    public static string FilePath => Path.Combine(ConfigDir, "settings.json");
    public static string ThemesDir => Path.Combine(ConfigDir, "themes");

    /// <summary>Set when settings.json could not be read; the broken file is kept as settings.json.broken.</summary>
    public static string? LoadProblem { get; private set; }

    public static Settings Load()
    {
        LoadProblem = null;
        if (!File.Exists(FilePath)) return new();
        try
        {
            var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), ReadOptions) ?? new();
            s.Normalize();
            return s;
        }
        catch (Exception e)
        {
            // Don't let the next Save() silently destroy a file the user was in the middle of editing.
            try { File.Copy(FilePath, FilePath + ".broken", overwrite: true); } catch { /* best effort */ }
            LoadProblem = $"settings.json has an error ({e.Message.Split('\n')[0]}) — using defaults. Your file was kept as settings.json.broken.";
            return new();
        }
    }

    /// <summary>Keeps hand-edited values inside sane bounds.</summary>
    public void Normalize()
    {
        EditorFontSize = Math.Clamp(EditorFontSize, 8, 40);
        TerminalFontSize = Math.Clamp(TerminalFontSize, 8, 32);
        TabSize = Math.Clamp(TabSize, 1, 16);
        if (Theme is "Liquid Glass" or "Liquid Glass Night") Theme = "Vanta Night";
        if (AutoSave is not ("off" or "afterDelay" or "onFocusLost")) AutoSave = "off";
        if (MarkdownMode is not ("split" or "preview" or "editor")) MarkdownMode = "split";
        if (Agent is not ("claude" or "codex")) Agent = "claude";
        if (AgentMode is not ("read" or "edit")) AgentMode = "read";
        RecentFolders ??= [];
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var header = "// LuminaIDE settings. Edit and save this file - changes apply immediately.\n" +
                         "// Changing a setting from the Settings page rewrites the file and drops comments like these.\n" +
                         "// Every option is documented in docs/CONFIG.md.\n";
            File.WriteAllText(FilePath, header + JsonSerializer.Serialize(this, WriteOptions) + "\n");
        }
        catch { /* settings are a convenience; never fail the app over them */ }
    }

    public void AddRecent(string folder)
    {
        RecentFolders.RemoveAll(f => f == folder);
        RecentFolders.Insert(0, folder);
        if (RecentFolders.Count > 8) RecentFolders.RemoveRange(8, RecentFolders.Count - 8);
    }
}
