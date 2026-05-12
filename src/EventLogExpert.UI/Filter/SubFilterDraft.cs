// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Filter;

public sealed class SubFilterDraft
{
    public FilterConditionDraft Condition { get; set; } = new();

    public FilterId Id { get; init; } = FilterId.Create();

    public bool JoinWithAny { get; set; }

    public SubFilter ToSubFilter() => new(Condition.ToCondition(), JoinWithAny);
}
