// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.ComponentModel.DataAnnotations;

namespace EventLogExpert.Library.Models;

public record SettingsModel
{
    [Range(-14, 14, ErrorMessage = "Invalid Time Zone")]
    public int TimeZoneOffset { get; set; }
}
