// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Filtering.Evaluation;

public sealed record DateFilter
{
    public DateTime? After { get; set; }

    public DateTime? Before { get; set; }

    public bool IsEnabled { get; set; } = true;
}
