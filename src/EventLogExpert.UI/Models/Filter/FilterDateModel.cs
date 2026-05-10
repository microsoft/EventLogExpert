// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Models;

public sealed record FilterDateModel
{
    public DateTime? After { get; set; }

    public DateTime? Before { get; set; }

    public bool IsEnabled { get; set; } = true;
}
