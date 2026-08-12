// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Filtering.Evaluation;

public sealed record DateFilter
{
    public DateTime? After { get; init; }

    public DateTime? Before { get; init; }

    public bool IsEnabled { get; init; } = true;
}
