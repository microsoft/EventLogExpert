// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Basic;
using EventLogExpert.Filtering.Common.Filtering;

namespace EventLogExpert.Filtering.Drafts;

public sealed class FilterComparisonDraft
{
    /// <summary>The named EventData field this row targets; meaningful only when <see cref="Property" /> is EventData.</summary>
    public string? EventDataFieldName { get; set; }

    public bool HasUsableManyValues =>
        Operator is ComparisonOperator.Contains or ComparisonOperator.NotContains
            ? Values.Any(value => !string.IsNullOrEmpty(value))
            : Values.Count > 0;

    public MatchMode MatchMode { get; set; }

    public ComparisonOperator Operator { get; set; }

    public EventProperty Property { get; set; } = EventProperty.Id;

    /// <summary>The structured UserData path this row targets; meaningful only when <see cref="Property" /> is UserData.</summary>
    public string? UserDataFieldName { get; set; }

    public string? Value { get; set; }

    public List<string> Values { get; set; } = [];

    public static FilterComparisonDraft FromComparison(FilterComparison comparison) =>
        new()
        {
            Property = comparison.Property,
            Operator = comparison.Operator,
            MatchMode = comparison.MatchMode,
            Value = comparison.Value,
            Values = [.. comparison.Values],
            EventDataFieldName = comparison.EventDataFieldName,
            UserDataFieldName = comparison.UserDataFieldName
        };

    public void ChangeProperty(EventProperty property)
    {
        Property = property;
        Value = null;
        Values.Clear();
        EventDataFieldName = null;
        UserDataFieldName = null;
    }

    public FilterComparison ToComparison() =>
        new()
        {
            Property = Property,
            Operator = Operator,
            MatchMode = MatchMode,
            Value = Value,
            Values = [.. Values],
            EventDataFieldName = EventDataFieldName,
            UserDataFieldName = UserDataFieldName
        };
}
