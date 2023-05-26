// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Library.Models;

public record FilterDateModel
{
    public DateTime After { get; set; }

    public DateTime Before { get; set; }

    public TimeZoneInfo TimeZoneInfo { get; set; } = null!;

    public bool IsEnabled { get; set; } = true;
}
