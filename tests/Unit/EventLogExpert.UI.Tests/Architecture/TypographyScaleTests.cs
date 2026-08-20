// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Text.RegularExpressions;

namespace EventLogExpert.UI.Tests.Architecture;

public sealed partial class TypographyScaleTests
{
    // The distinct raw font-size values that predate the type-scale migration. New component CSS should use the
    // --font-size-* tokens (src/EventLogExpert.UI/wwwroot/app.css :root). This freezes the set of distinct raw VALUES
    // (not the declaration count), so it fails on any NEW distinct value; remove entries as the app-wide follow-up
    // migrates components to tokens.
    private static readonly HashSet<string> s_allowedRawFontSizes = new(StringComparer.Ordinal)
    {
        ".75rem", ".8rem", ".85rem", ".9rem", ".8em", ".85em",
        "0.65rem", "0.7rem", "0.75rem", "0.78rem", "0.8rem", "0.85rem", "0.9rem", "0.95rem",
        "0.75em", "0.85em", "0.9em", "0.95em",
        "1rem", "1.05rem", "1.1rem", "1.1em", "1.2rem", "1.25rem", "1.4rem", "1.6rem",
        "11px", "12px", "13px", "14px", "10pt"
    };

    // The only sanctioned font-size var() forms; anything else (a typo like --font-size-huge, or a non-size var used as
    // a font-size) is treated as drift.
    private static readonly HashSet<string> s_allowedTokens = new(StringComparer.Ordinal)
    {
        "var(--font-size-xs)", "var(--font-size-sm)", "var(--font-size-md)",
        "var(--font-size-base)", "var(--font-size-lg)", "var(--font-size-xl)"
    };

    [Fact]
    public void RazorCssFontSizes_UseTokensOrTheFrozenBaseline()
    {
        string sourceRoot = Path.Combine(RepoRoot(), "src");
        string uiWwwroot = Path.Combine(sourceRoot, "EventLogExpert.UI", "wwwroot");

        // First-party component sheets plus the two migrated global sheets (the highest-blast-radius chrome CSS). Vendor
        // bundles live under EventLogExpert/wwwroot/css and are not *.razor.css, so they are excluded.
        var files = Directory.EnumerateFiles(sourceRoot, "*.razor.css", SearchOption.AllDirectories).ToList();
        files.Add(Path.Combine(uiWwwroot, "app.css"));
        files.Add(Path.Combine(uiWwwroot, "Banner", "banner.css"));

        Regex fontSize = FontSizeRegex();
        var offenders = new List<string>();

        foreach (string file in files)
        {
            foreach (Match match in fontSize.Matches(File.ReadAllText(file)))
            {
                string value = match.Groups[1].Value.Trim();

                if (s_allowedTokens.Contains(value)) { continue; }

                if (!s_allowedRawFontSizes.Contains(value))
                {
                    offenders.Add($"{Path.GetFileName(file)}: font-size: {value}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "New raw font-size value(s) found in first-party CSS. Use a --font-size-* token from " +
            "src/EventLogExpert.UI/wwwroot/app.css :root instead:\n" + string.Join("\n", offenders));
    }

    [GeneratedRegex(@"font-size:\s*([^;}]+)[;}]")]
    private static partial Regex FontSizeRegex();

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EventLogExpert.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        return directory.FullName;
    }
}
