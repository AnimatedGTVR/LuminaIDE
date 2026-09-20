using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace LuminaIDE;

public sealed record Entry(string name, string path, bool is_dir);
public sealed record Hit(string path, int line, string text);

/// <summary>One normalized event from the Rust agent runner (see core/src/agent.rs).</summary>
public sealed record AgentEvent(string k, string? t, string? id, string? name, string? detail, bool? ok, double? cost, string? session);

/// <summary>Rust engine (lumina_core). Strings cross as UTF-8; lists come back as JSON.</summary>
internal static class Core
{
    const string Lib = "lumina_core";

    [DllImport(Lib)] static extern IntPtr lumina_list_dir([MarshalAs(UnmanagedType.LPUTF8Str)] string dir);
    [DllImport(Lib)] static extern IntPtr lumina_read_file([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [DllImport(Lib)] static extern int lumina_write_file([MarshalAs(UnmanagedType.LPUTF8Str)] string path,
                                                         [MarshalAs(UnmanagedType.LPUTF8Str)] string content);
    [DllImport(Lib)] static extern IntPtr lumina_search([MarshalAs(UnmanagedType.LPUTF8Str)] string root,
                                                        [MarshalAs(UnmanagedType.LPUTF8Str)] string query);
    [DllImport(Lib)] static extern void lumina_free_string(IntPtr s);
    [DllImport(Lib)] static extern int lumina_agent_available([MarshalAs(UnmanagedType.LPUTF8Str)] string agent);
    [DllImport(Lib)] static extern IntPtr lumina_agent_start([MarshalAs(UnmanagedType.LPUTF8Str)] string config);
    [DllImport(Lib)] static extern IntPtr lumina_agent_poll(IntPtr handle);
    [DllImport(Lib)] static extern void lumina_agent_stop(IntPtr handle);
    [DllImport(Lib)] static extern void lumina_agent_free(IntPtr handle);
    [DllImport(Lib)] static extern IntPtr lumina_list_files([MarshalAs(UnmanagedType.LPUTF8Str)] string root, int limit);
    [DllImport(Lib)] static extern IntPtr lumina_markdown([MarshalAs(UnmanagedType.LPUTF8Str)] string text);
    [DllImport(Lib)] static extern int lumina_register_license([MarshalAs(UnmanagedType.LPUTF8Str)] string json);
    [DllImport(Lib)] static extern void lumina_clear_licenses();
    [DllImport(Lib)] static extern IntPtr lumina_detect_license([MarshalAs(UnmanagedType.LPUTF8Str)] string text);
    [DllImport(Lib)] static extern int lumina_register_language([MarshalAs(UnmanagedType.LPUTF8Str)] string json);
    [DllImport(Lib)] static extern void lumina_clear_languages();
    [DllImport(Lib)] static extern IntPtr lumina_language_for_path([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [DllImport(Lib)] static extern IntPtr lumina_tokenize([MarshalAs(UnmanagedType.LPUTF8Str)] string lang,
                                                          [MarshalAs(UnmanagedType.LPUTF8Str)] string text);

    static string? Take(IntPtr p)
    {
        if (p == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUTF8(p); }
        finally { lumina_free_string(p); }
    }

    static T[] Decode<T>(IntPtr p) =>
        Take(p) is { } json ? JsonSerializer.Deserialize<T[]>(json) ?? [] : [];

    /// <summary>Relative paths of every file under <paramref name="root"/> (gitignore-aware), for quick open.</summary>
    public static string[] ListFiles(string root, int limit = 20000) => Decode<string>(lumina_list_files(root, limit));
    public static Entry[] ListDir(string dir) => Decode<Entry>(lumina_list_dir(dir));
    public static string? ReadFile(string path) => Take(lumina_read_file(path));
    public static bool WriteFile(string path, string content) => lumina_write_file(path, content) == 0;
    public static Hit[] Search(string root, string query) => Decode<Hit>(lumina_search(root, query));

    /// <summary>Block tree for a Markdown document (see core/src/markdown.rs). Dispose the result.</summary>
    public static JsonDocument? Markdown(string text) => Take(lumina_markdown(text)) is { } json ? JsonDocument.Parse(json) : null;

    public static bool AgentAvailable(string agent) => lumina_agent_available(agent) == 1;
    public static IntPtr AgentStart(string configJson) => lumina_agent_start(configJson);
    public static AgentEvent[] AgentPoll(IntPtr handle) => Decode<AgentEvent>(lumina_agent_poll(handle));
    public static void AgentStop(IntPtr handle) => lumina_agent_stop(handle);
    public static void AgentFree(IntPtr handle) => lumina_agent_free(handle);

    public static bool RegisterLicense(string json) => lumina_register_license(json) == 0;
    public static void ClearLicenses() => lumina_clear_licenses();
    /// <summary>The id (e.g. "MIT") of the license this text is, or null when it isn't one we know.</summary>
    public static string? DetectLicense(string text) => Take(lumina_detect_license(text));

    public static bool RegisterLanguage(string json) => lumina_register_language(json) == 0;
    public static void ClearLanguages() => lumina_clear_languages();
    public static string? LanguageForPath(string path) => Take(lumina_language_for_path(path));

    /// <summary>Flat [start, len, kind, ...] triples in UTF-16 offsets; empty for unknown languages.</summary>
    public static int[] Tokenize(string lang, string text) => Decode<int>(lumina_tokenize(lang, text));
}

/// <summary>C++ PTY session (luminaterm).</summary>
internal sealed class Terminal : IDisposable
{
    const string Lib = "luminaterm";

    [DllImport(Lib)] static extern IntPtr lt_open(int rows, int cols, [MarshalAs(UnmanagedType.LPUTF8Str)] string cwd);
    [DllImport(Lib)] static extern int lt_read(IntPtr h, byte[] buf, int cap);
    [DllImport(Lib)] static extern int lt_write(IntPtr h, byte[] data, int len);
    [DllImport(Lib)] static extern void lt_close(IntPtr h);

    IntPtr _h;
    readonly byte[] _buf = new byte[8192];
    readonly Decoder _utf8 = Encoding.UTF8.GetDecoder();

    public static Terminal? Open(string cwd)
    {
        var h = lt_open(24, 500, cwd); // wide on purpose: the UI wraps text itself, and a narrow PTY makes the shell redraw long lines
        return h == IntPtr.Zero ? null : new Terminal { _h = h };
    }

    /// <summary>Returns text produced since last call, or null once the shell has exited.</summary>
    public string? Poll()
    {
        if (_h == IntPtr.Zero) return null;
        var sb = new StringBuilder();
        while (true)
        {
            int n = lt_read(_h, _buf, _buf.Length);
            if (n < 0) return sb.Length > 0 ? sb.ToString() : null;
            if (n == 0) return sb.ToString();
            var chars = new char[_utf8.GetCharCount(_buf, 0, n)];
            _utf8.GetChars(_buf, 0, n, chars, 0);
            sb.Append(chars);
        }
    }

    public void Send(string text)
    {
        if (_h == IntPtr.Zero) return;
        if (Platform.IsWindows) text = text.Replace("\r\n", "\n").Replace("\n", "\r\n");
        var bytes = Encoding.UTF8.GetBytes(text);
        lt_write(_h, bytes, bytes.Length);
    }

    public void Dispose()
    {
        if (_h == IntPtr.Zero) return;
        lt_close(_h);
        _h = IntPtr.Zero;
    }
}
