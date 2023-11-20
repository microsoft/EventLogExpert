// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace EventLogExpert.UI.Models;

public record GitReleaseModel
{
    [JsonPropertyName("name")] public string Version { get; set; } = null!;

    [JsonPropertyName("prerelease")] public bool IsPrerelease { get; set; }

    [JsonPropertyName("published_at")] public DateTime ReleaseDate { get; set; }

    [JsonPropertyName("assets")] public List<GitReleaseAsset> Assets { get; set; } = null!;

    [JsonPropertyName("body")] public string RawChanges { get; set; } = null!;
    public string Changes => ParseChanges();

    private string ParseChanges()
    {
        List<string> changes = new List<string>();

        // Use regular expression to match lines starting with '*'
        string pattern = @"^\*\s(.+)$";
        Regex regex = new Regex(pattern, RegexOptions.Multiline);
        MatchCollection matches = regex.Matches(this.RawChanges);

        foreach (Match match in matches)
        {
            string changeDescription = match.Groups[1].Value.Trim();

            // https://www.shellhacks.com/git-get-short-hash-sha-1-from-long-hash-head-log
            if (changeDescription.Length > 40)
            {
                changeDescription = changeDescription.Substring(40).Trim();
            }

            changes.Add(changeDescription);
        }

        return String.Join(Environment.NewLine, changes);
    }
}

public record GitReleaseAsset
{
    [JsonPropertyName("name")] public string Name { get; set; } = null!;

    [JsonPropertyName("browser_download_url")] public string Uri { get; set; } = null!;
}
