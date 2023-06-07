// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace EventLogExpert.UI.Models;

public record SettingsModel
{
    public string TimeZoneId { get; set; } = TimeZoneInfo.Local.Id;

    [JsonIgnore]
    public TimeZoneInfo TimeZoneInfo => TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);

    [JsonIgnore]
    public IList<string> DisabledDatabases { get; set; } = new List<string>();

    [JsonIgnore]
    public bool IsPrereleaseEnabled { get; set; }
}
