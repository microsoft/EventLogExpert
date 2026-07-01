// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Common.Filtering;

namespace EventLogExpert.Runtime.DebugLog;

public sealed record DebugLogFilter(
    DebugLogFilterField Field,
    ComparisonOperator Operator,
    MatchMode MatchMode,
    bool IsExcluded,
    IReadOnlyList<string> Values)
{
    public bool IsComplete =>
        Field == DebugLogFilterField.Message
            ? Values.Count > 0 && !string.IsNullOrWhiteSpace(Values[0])
            : Values.Count > 0;
}
