// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Filter;

/// <summary>
///     Mutable editor mirror of <see cref="FilterCondition" /> used by the Basic-filter UI. Exists because Blazor two-way
///     binding requires get/set properties; the immutable <see cref="FilterCondition" /> is materialized via
///     <see cref="ToCondition" /> on save.
/// </summary>
public sealed class FilterConditionDraft
{
    public FilterCategory Category { get; set; }

    public FilterEvaluator Evaluator { get; set; }

    public string? Value { get; set; }

    public List<string> Values { get; set; } = [];

    public static FilterConditionDraft FromCondition(FilterCondition condition) =>
        new()
        {
            Category = condition.Category,
            Evaluator = condition.Evaluator,
            Value = condition.Value,
            Values = [.. condition.Values]
        };

    /// <summary>
    ///     Mutates the draft to switch to <paramref name="category" />, clearing <see cref="Value" /> and
    ///     <see cref="Values" /> because the available value space differs across categories. Mirrors the intent of
    ///     <see cref="FilterCondition.WithCategory" /> on the immutable side.
    /// </summary>
    public void ChangeCategory(FilterCategory category)
    {
        Category = category;
        Value = null;
        Values.Clear();
    }

    public FilterCondition ToCondition() =>
        new()
        {
            Category = Category,
            Evaluator = Evaluator,
            Value = Value,
            Values = [.. Values]
        };
}
