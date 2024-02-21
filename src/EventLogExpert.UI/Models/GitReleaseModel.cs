// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace EventLogExpert.UI.Models;

public readonly partial record struct GitReleaseModel
{
    [JsonPropertyName("name")] public string Version { get; init; }

    [JsonPropertyName("prerelease")] public bool IsPreRelease { get; init; }

    [JsonPropertyName("published_at")] public DateTime ReleaseDate { get; init; }

    [JsonPropertyName("assets")] public List<GitReleaseAsset> Assets { get; init; }

    [JsonPropertyName("body")] public string RawChanges { get; init; }

    public List<string> Changes
    {
        get
        {
            List<string> changes = [];

            Regex regex = SplitChangeLog();
            MatchCollection matches = regex.Matches(RawChanges);

            foreach (var match in matches.Cast<Match>())
            {
                string changeDescription = match.Groups[1].Value.Trim();

                // https://www.shellhacks.com/git-get-short-hash-sha-1-from-long-hash-head-log
                if (changeDescription.Length > 40)
                {
                    changeDescription = changeDescription[40..].Trim();
                }

                changes.Add(changeDescription);
            }

            return changes;
        }
    }

    /// <summary>Use regular expression to match lines starting with '*'</summary>
    [GeneratedRegex(@"^\*\s(.+)$", RegexOptions.Multiline)]
    private static partial Regex SplitChangeLog();
}

public readonly record struct GitReleaseAsset()
{
    [JsonPropertyName("name")] public string Name { get; init; } = null!;

    [JsonPropertyName("browser_download_url")] public string Uri { get; init; }
}
