// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace EventLogExpert.Library.Models;

public record GitReleaseModel
{
    [JsonPropertyName("name")] public string Version { get; set; } = null!;

    [JsonPropertyName("assets")] public List<GitReleaseAsset> Assets { get; set; } = null!;
}

public record GitReleaseAsset
{
    [JsonPropertyName("name")] public string Name { get; set; } = null!;

    [JsonPropertyName("browser_download_url")] public string Uri { get; set; } = null!;
}
