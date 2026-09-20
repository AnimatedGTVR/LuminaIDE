using System.Text.Json;
using LuminaIDE;
using Xunit;

namespace LuminaIDE.Tests;

public class LicenseFileNameTests
{
    [Theory]
    [InlineData("LICENSE", true)]
    [InlineData("LICENSE.md", true)]
    [InlineData("license.txt", true)]
    [InlineData("LICENCE", true)]
    [InlineData("LICENSE-MIT", true)]
    [InlineData("LICENSE-APACHE", true)]
    [InlineData("LICENSE_third_party.txt", true)]
    [InlineData("COPYING", true)]
    [InlineData("COPYING.LESSER", true)]
    [InlineData("UNLICENSE", true)]
    [InlineData("UNLICENSE.txt", true)]
    [InlineData("OFL-Inter.txt", true)]
    [InlineData("license-checker.js", false)]     // code that merely has "license" in its name
    [InlineData("LICENSE.rs", false)]
    [InlineData("licensed-under.txt", false)]     // "licensed", not "license"
    [InlineData("README.md", false)]
    [InlineData("copyright.txt", false)]
    [InlineData("NOTICE", false)]
    [InlineData("main.rs", false)]
    public void Recognises_license_files_by_name(string name, bool expected) =>
        Assert.Equal(expected, LicenseFiles.IsLicenseFile("/project/" + name));

    [Theory]
    [InlineData("/project/licenses/anything.txt", true)]
    [InlineData("/project/LICENSES/Some-Dep.md", true)]
    [InlineData("/project/licenses/build.py", false)]  // only text files
    [InlineData("/project/docs/anything.txt", false)]
    public void Any_text_file_in_a_licenses_folder_counts(string path, bool expected) =>
        Assert.Equal(expected, LicenseFiles.IsLicenseFile(path));
}

public class LicenseDataTests
{
    static string Dir()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "extensions", "license-info", "licenses");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("extensions/license-info/licenses not found above " + AppContext.BaseDirectory);
    }

    static IEnumerable<LicenseInfo> All() =>
        Directory.GetFiles(Dir(), "*.json").Select(f => JsonSerializer.Deserialize<LicenseInfo>(File.ReadAllText(f))!);

    [Fact]
    public void Every_tag_used_by_a_license_has_plain_language_wording()
    {
        var licenses = All().ToList();
        Assert.True(licenses.Count >= 20);
        foreach (var l in licenses)
        {
            foreach (var t in l.Permissions) Assert.True(LicenseTerms.Permissions.ContainsKey(t), $"{l.Id}: no wording for permission '{t}'");
            foreach (var t in l.Conditions) Assert.True(LicenseTerms.Conditions.ContainsKey(t), $"{l.Id}: no wording for condition '{t}'");
            foreach (var t in l.Limitations) Assert.True(LicenseTerms.Limitations.ContainsKey(t), $"{l.Id}: no wording for limitation '{t}'");
        }
    }

    [Fact]
    public void Wording_is_written_for_people_not_lawyers()
    {
        foreach (var table in new[] { LicenseTerms.Permissions, LicenseTerms.Conditions, LicenseTerms.Limitations })
            foreach (var (tag, (label, help)) in table)
            {
                Assert.False(string.IsNullOrWhiteSpace(label), tag);
                Assert.True(help.Length is > 20 and < 200, $"{tag}: explanation should be one clear sentence");
                Assert.EndsWith(".", help);
            }
    }

    [Fact]
    public void Every_license_has_a_summary_and_sensible_facts()
    {
        foreach (var l in All())
        {
            Assert.True(l.Summary.Length > 40, $"{l.Id}: summary too short");
            Assert.StartsWith("https://", l.Url);
            // Every real open-source license here lets you do at least the basics; a mistake in the data would be worth catching.
            Assert.Contains("distribution", l.Permissions);
            Assert.Contains("modifications", l.Permissions);
            // Anything with a copyleft category must carry a same-license style condition.
            if (l.Category.Contains("copyleft", StringComparison.OrdinalIgnoreCase))
                Assert.Contains(l.Conditions, c => c.StartsWith("same-license") || c == "network-use-disclose");
        }
    }

    [Fact]
    public void Facts_for_well_known_licenses_match_what_people_expect()
    {
        var byId = All().ToDictionary(l => l.Id);
        Assert.Equal(["include-copyright"], byId["MIT"].Conditions);
        Assert.Empty(byId["Unlicense"].Conditions);
        Assert.Contains("patent-use", byId["Apache-2.0"].Permissions);
        Assert.Contains("same-license", byId["GPL-3.0"].Conditions);
        Assert.Contains("disclose-source", byId["GPL-3.0"].Conditions);
        Assert.Contains("network-use-disclose", byId["AGPL-3.0"].Conditions);
        Assert.Contains("same-license--library", byId["LGPL-3.0"].Conditions);
        Assert.Contains("same-license--file", byId["MPL-2.0"].Conditions);
        Assert.Contains("trademark-use", byId["Apache-2.0"].Limitations);
        Assert.Contains("patent-use", byId["CC0-1.0"].Limitations);   // CC0 explicitly grants no patent rights
        Assert.DoesNotContain("commercial-use", byId["MIT"].Limitations);
    }
}
