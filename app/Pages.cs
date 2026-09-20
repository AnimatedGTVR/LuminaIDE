using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace LuminaIDE;

/// <summary>Static pages shown over the editor: release notes.</summary>
static class Pages
{
    /// <summary>CHANGELOG.md ships next to the executable; in a source checkout it sits four folders up.</summary>
    public static string? ChangelogPath()
    {
        foreach (var candidate in new[]
        {
            Path.Combine(AppContext.BaseDirectory, "CHANGELOG.md"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "CHANGELOG.md")),
        })
            if (File.Exists(candidate)) return candidate;
        return null;
    }

    public static string ReadChangelog() => ChangelogPath() is { } p ? File.ReadAllText(p) : "# Release notes\n\nCHANGELOG.md was not found next to the application.";

    /// <summary>The newest release section as (heading, bullet points), for the welcome page.</summary>
    public static (string Heading, string[] Items) LatestRelease()
    {
        string heading = $"v{AppInfo.Version}";
        var items = new List<string>();
        bool inSection = false;
        foreach (var raw in ReadChangelog().Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.StartsWith("## "))
            {
                if (inSection) break;
                if (line.Contains("Unreleased", StringComparison.OrdinalIgnoreCase)) continue; // skip the placeholder section
                inSection = true;
                heading = line[3..].Trim().Replace("[", "").Replace("]", "");
                continue;
            }
            if (inSection && line.StartsWith("- ")) items.Add(line[2..].Replace("**", "").Replace("`", ""));
        }
        return (heading, items.Take(5).ToArray());
    }
}
