// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace EventLogExpert.UI.Models;

public record GitReleaseModel
{
    [JsonPropertyName("name")] public string Version { get; set; } = null!;

    [JsonPropertyName("prerelease")] public bool IsPrerelease { get; set; }

    [JsonPropertyName("published_at")] public DateTime ReleaseDate { get; set; }

    [JsonPropertyName("assets")] public List<GitReleaseAsset> Assets { get; set; } = null!;
}

public record GitReleaseAsset
{
    [JsonPropertyName("name")] public string Name { get; set; } = null!;

    [JsonPropertyName("browser_download_url")] public string Uri { get; set; } = null!;
}
