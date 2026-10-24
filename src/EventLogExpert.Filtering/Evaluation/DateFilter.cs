// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Filtering.Evaluation;

public sealed record DateFilter
{
    public DateTime? After { get; init; }

    public DateTime? Before { get; init; }

    public bool IsEnabled { get; init; } = true;

    /// <summary>Inclusive range test; a null bound leaves that side of the range open.</summary>
    internal bool Contains(DateTime timeCreated) =>
        (After is null || timeCreated >= After) && (Before is null || timeCreated <= Before);
}
