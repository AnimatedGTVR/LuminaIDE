using System.Runtime.InteropServices;
using System.Text;

namespace LuminaIDE;

/// <summary>Command-line entry points that need no window: --version, --help and --selftest.</summary>
static class Cli
{
    public const string Usage = """
        LuminaIDE — a small, fast code editor.

        Usage: luminaide [folder] [file...] [options]

          --theme "NAME"        start with a theme (e.g. "Vanta Night")
          --view NAME           open a side panel: explorer, search, web, extensions
          --terminal            open the terminal panel
          --agent               open the AI agent panel
          --ask "PROMPT"        open the agent panel and send a first prompt
          --run                 run the opened file (same as F5)
          --page NAME           open a page: settings, changelog, palette, quickopen, license:MIT
          --screenshot FILE     render the window to a PNG and quit (see --wait MS)
          --selftest            check that the native libraries and shell work, then exit
          -v, --version         print the version
          -h, --help            print this help

        Settings: see docs/CONFIG.md. Config folder: {CONFIG}
        """;

    [DllImport("kernel32.dll")] static extern bool AttachConsole(int processId);

    /// <summary>GUI apps on Windows have no console; borrow the parent's so output shows up in cmd/PowerShell.</summary>
    public static void AttachConsole()
    {
        if (Platform.IsWindows) { try { AttachConsole(-1); } catch { /* no console to attach to */ } }
    }

    public static string UsageText => Usage.Replace("{CONFIG}", Settings.ConfigDir);
}

/// <summary>
/// Exercises everything that crosses into native code, without opening a window. CI runs it on every
/// operating system, and it is handy after installing: <c>luminaide --selftest</c>.
/// </summary>
static class SelfTest
{
    public static int Run()
    {
        int failures = 0;
        void Check(string name, Func<bool> test)
        {
            try
            {
                bool ok = test();
                Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {name}");
                if (!ok) failures++;
            }
            catch (Exception e)
            {
                Console.WriteLine($"  FAIL {name}: {e.GetType().Name}: {e.Message}");
                failures++;
            }
        }

        Console.WriteLine($"{AppInfo.Name} {AppInfo.Version} on {Platform.Name} ({RuntimeInformation.OSArchitecture}, {RuntimeInformation.FrameworkDescription})");
        var ext = new ExtensionHost();

        Check("Rust core loads and lists a folder", () => Core.ListDir(AppContext.BaseDirectory).Length > 0);
        Check("built-in extensions found (80+ languages, 7+ themes)", () => { ext.Load(); return ext.Languages.Count >= 80 && ext.Themes.Count >= 7; });
        Check("language detection and tokenizer", () => Core.LanguageForPath("main.rs") == "rust" && Core.Tokenize("rust", "fn main() { let x = 1; }").Length >= 9);
        Check("Nix and HTML languages", () => Core.LanguageForPath("flake.nix") == "nix" && Core.LanguageForPath("index.html") == "html");
        Check("license detection (MIT)", () => ext.Licenses.Count >= 20 && Core.DetectLicense("Permission is hereby granted, free of charge, to any person obtaining a copy of this software. The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.") == "MIT");
        Check("Markdown parser", () => { using var d = Core.Markdown("# Hi **there**\n\n- a\n- b"); return d is not null && d.RootElement.GetArrayLength() == 2; });
        Check("file search and quick-open listing", () => Core.ListFiles(AppContext.BaseDirectory, 50).Length > 0);
        Check("default settings are sane", () => { var s = new Settings(); s.Normalize(); return s.TabSize == 4 && s.Theme.Length > 0; });
        Check("built-in shell runs a command", () =>
        {
            using var term = Terminal.Open(Path.GetTempPath());
            if (term is null) return false;
            term.Send("echo lumina-selftest-ok\n");
            var output = new StringBuilder();
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                if (term.Poll() is { } chunk) output.Append(chunk); else break;
                if (output.ToString().Contains("lumina-selftest-ok")) return true;
                Thread.Sleep(50);
            }
            return output.ToString().Contains("lumina-selftest-ok");
        });

        Console.WriteLine(failures == 0 ? "selftest passed" : $"selftest FAILED ({failures})");
        return failures == 0 ? 0 : 1;
    }
}
