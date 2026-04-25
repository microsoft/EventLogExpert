// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Models;

/// <summary>
///     Mutable editor mirror of <see cref="BasicFilterCriteria" /> used by the Basic-filter UI. Exists because Blazor
///     two-way binding requires get/set properties; the immutable <see cref="BasicFilterCriteria" /> is materialized via
///     <see cref="ToCriteria" /> on save.
/// </summary>
public sealed class BasicFilterCriteriaDraft
{
    public FilterCategory Category { get; set; }

    public FilterEvaluator Evaluator { get; set; }

    public string? Value { get; set; }

    public List<string> Values { get; set; } = [];

    public static BasicFilterCriteriaDraft FromCriteria(BasicFilterCriteria criteria) =>
        new()
        {
            Category = criteria.Category,
            Evaluator = criteria.Evaluator,
            Value = criteria.Value,
            Values = [.. criteria.Values]
        };

    /// <summary>
    ///     Mutates the draft to switch to <paramref name="category" />, clearing <see cref="Value" /> and
    ///     <see cref="Values" /> because the available value space differs across categories. Mirrors the intent of
    ///     <see cref="BasicFilterCriteria.WithCategory" /> on the immutable side.
    /// </summary>
    public void ChangeCategory(FilterCategory category)
    {
        Category = category;
        Value = null;
        Values.Clear();
    }

    public BasicFilterCriteria ToCriteria() =>
        new()
        {
            Category = Category,
            Evaluator = Evaluator,
            Value = Value,
            Values = [.. Values]
        };
}
