// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Text.RegularExpressions;

namespace EventLogExpert.UI.Services;

/// <summary>
/// Normalizes a GitHub release body into a clean Markdown document that can be
/// rendered by <see cref="ReleaseNotesMarkdownRenderer"/>.
///
/// Future releases are hand-authored in rich Markdown (headings, bold, lists,
/// links) and pass through unchanged. Legacy releases used a flat list of
/// <c>* &lt;commit-id&gt; description</c> bullets; this normalizer strips the
/// commit-id prefix from those lines so they render as clean bullet items.
/// </summary>
public static partial class ReleaseNotesNormalizer
{
    public static string Normalize(string? rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return string.Empty;
        }

        var working = rawBody.Replace("\r\n", "\n").Replace('\r', '\n');

        working = BulletRegex().Replace(working, match => $"- {match.Groups[1].Value.Trim()}");

        return working.Trim();
    }

    /// <summary>
    /// Matches a <c>* </c> bullet whose content optionally begins with a commit
    /// identifier — either a 40-character SHA or a Markdown link
    /// (e.g. <c>[abc1234](https://...)</c>) — and rewrites it as a clean
    /// <c>- description</c> bullet. Bullets without a commit prefix are simply
    /// converted from <c>*</c> to <c>-</c> style for consistency.
    /// </summary>
    [GeneratedRegex(@"^\*\s+(?:\[[0-9a-f]{6,}\]\([^)\s]+\)\s+|[0-9a-f]{40}\s+)?(.+)$",
        RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex BulletRegex();
}

