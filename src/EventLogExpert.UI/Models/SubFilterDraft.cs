// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Models;

public sealed class SubFilterDraft
{
    public FilterDataDraft Data { get; set; } = new();

    public FilterId Id { get; init; } = FilterId.Create();

    public bool JoinWithAny { get; set; }

    public SubFilter ToSubFilter() => new(Data.ToData(), JoinWithAny);
}
