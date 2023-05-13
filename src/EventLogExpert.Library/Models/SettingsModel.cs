// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Library.Models;

public record SettingsModel
{
    public string TimeZoneId { get; set; } = string.Empty;
}
