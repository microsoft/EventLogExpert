// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Basic;
using EventLogExpert.Filtering.Common;

namespace EventLogExpert.Filtering.Drafts;

public sealed class FilterComparisonDraft
{
    public MatchMode MatchMode { get; set; }

    public ComparisonOperator Operator { get; set; }

    public EventProperty Property { get; set; }

    public string? Value { get; set; }

    public List<string> Values { get; set; } = [];

    public static FilterComparisonDraft FromComparison(FilterComparison comparison) =>
        new()
        {
            Property = comparison.Property,
            Operator = comparison.Operator,
            MatchMode = comparison.MatchMode,
            Value = comparison.Value,
            Values = [.. comparison.Values]
        };

    public void ChangeProperty(EventProperty property)
    {
        Property = property;
        Value = null;
        Values.Clear();
    }

    public FilterComparison ToComparison() =>
        new()
        {
            Property = Property,
            Operator = Operator,
            MatchMode = MatchMode,
            Value = Value,
            Values = [.. Values]
        };
}
