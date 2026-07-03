// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Common.Filtering;
using EventLogExpert.Runtime.DebugLog;

namespace EventLogExpert.UI.DebugLog;

public sealed class DebugLogFilterDraft
{
    public DebugLogFilterField Field { get; set; } = DebugLogFilterField.Message;

    // Delegates to the immutable filter so the completeness rule has a single source of truth.
    public bool IsComplete => ToFilter().IsComplete;

    public bool IsExcluded { get; set; }

    public MatchMode MatchMode { get; set; } = MatchMode.Single;

    public ComparisonOperator Operator { get; set; } = ComparisonOperator.Contains;

    public List<string> Values { get; set; } = [];

    // A value-detached copy of an applied filter for edit-on-copy: editing the draft never mutates the applied form.
    public static DebugLogFilterDraft FromFilter(DebugLogFilter filter) => new()
    {
        Field = filter.Field,
        Operator = filter.Operator,
        MatchMode = filter.MatchMode,
        IsExcluded = filter.IsExcluded,
        Values = [.. filter.Values],
    };

    // ToFilter defaults IsEnabled to true (a new filter starts enabled); the caller preserves an edited chip's enable state.
    public DebugLogFilter ToFilter() => new(Field, Operator, MatchMode, IsExcluded, [.. Values]);
}
