// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Basic;
using EventLogExpert.Filtering.Persistence;

namespace EventLogExpert.Filtering.Drafts;

public sealed class SubFilterDraft
{
    public FilterComparisonDraft Comparison { get; set; } = new();

    public FilterId Id { get; init; } = FilterId.Create();

    public bool JoinWithAny { get; set; }

    public SubFilter ToSubFilter() => new(Comparison.ToComparison(), JoinWithAny);
}
