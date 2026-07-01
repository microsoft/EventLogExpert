// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Common.Filtering;
using EventLogExpert.Runtime.DebugLog;

namespace EventLogExpert.UI.DebugLog;

public sealed class DebugLogFilterDraft
{
    public DebugLogFilterField Field { get; set; } = DebugLogFilterField.Message;

    public Guid Id { get; } = Guid.NewGuid();

    // Delegates to the immutable filter so the completeness rule has a single source of truth.
    public bool IsComplete => ToFilter().IsComplete;

    public bool IsExcluded { get; set; }

    public MatchMode MatchMode { get; set; } = MatchMode.Single;

    public ComparisonOperator Operator { get; set; } = ComparisonOperator.Contains;

    public List<string> Values { get; set; } = [];

    public DebugLogFilter ToFilter() => new(Field, Operator, MatchMode, IsExcluded, [.. Values]);
}
