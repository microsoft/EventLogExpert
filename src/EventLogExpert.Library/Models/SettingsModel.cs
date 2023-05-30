// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace EventLogExpert.Library.Models;

public record SettingsModel
{
    public string TimeZoneId { get; set; } = TimeZoneInfo.Local.Id;

    [JsonIgnore]
    public TimeZoneInfo TimeZoneInfo => TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);

    public IList<string>? DisabledProviders { get; set; }

    public bool IsPrereleaseEnabled { get; set; }
}
