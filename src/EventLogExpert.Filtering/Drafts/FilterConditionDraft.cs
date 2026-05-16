// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Filtering.Drafts;

/// <summary>
///     Mutable editor mirror of <see cref="BasicFilterCondition" /> used by the Basic-filter UI. Exists because
///     Blazor two-way binding requires get/set properties; the immutable <see cref="BasicFilterCondition" /> is
///     materialized via <see cref="ToCondition" /> on save.
/// </summary>
public sealed class FilterConditionDraft
{
    public MatchMode MatchMode { get; set; }

    public ComparisonOperator Operator { get; set; }

    public EventProperty Property { get; set; }

    public string? Value { get; set; }

    public List<string> Values { get; set; } = [];

    public static FilterConditionDraft FromCondition(BasicFilterCondition condition) =>
        new()
        {
            Property = condition.Property,
            Operator = condition.Operator,
            MatchMode = condition.MatchMode,
            Value = condition.Value,
            Values = [.. condition.Values]
        };

    /// <summary>
    ///     Mutates the draft to switch to <paramref name="property" />, clearing <see cref="Value" /> and
    ///     <see cref="Values" /> because the available value space differs across properties. Mirrors the intent of
    ///     <see cref="BasicFilterCondition.WithProperty" /> on the immutable side.
    /// </summary>
    public void ChangeProperty(EventProperty property)
    {
        Property = property;
        Value = null;
        Values.Clear();
    }

    public BasicFilterCondition ToCondition() =>
        new()
        {
            Property = Property,
            Operator = Operator,
            MatchMode = MatchMode,
            Value = Value,
            Values = [.. Values]
        };
}
