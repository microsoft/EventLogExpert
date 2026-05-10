// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Models;

/// <summary>
///     Mutable editor mirror of <see cref="FilterData" /> used by the Basic-filter UI. Exists because Blazor two-way
///     binding requires get/set properties; the immutable <see cref="FilterData" /> is materialized via
///     <see cref="ToData" /> on save.
/// </summary>
public sealed class FilterDataDraft
{
    public FilterCategory Category { get; set; }

    public FilterEvaluator Evaluator { get; set; }

    public string? Value { get; set; }

    public List<string> Values { get; set; } = [];

    public static FilterDataDraft FromData(FilterData data) =>
        new()
        {
            Category = data.Category,
            Evaluator = data.Evaluator,
            Value = data.Value,
            Values = [.. data.Values]
        };

    /// <summary>
    ///     Mutates the draft to switch to <paramref name="category" />, clearing <see cref="Value" /> and
    ///     <see cref="Values" /> because the available value space differs across categories. Mirrors the intent of
    ///     <see cref="FilterData.WithCategory" /> on the immutable side.
    /// </summary>
    public void ChangeCategory(FilterCategory category)
    {
        Category = category;
        Value = null;
        Values.Clear();
    }

    public FilterData ToData() =>
        new()
        {
            Category = Category,
            Evaluator = Evaluator,
            Value = Value,
            Values = [.. Values]
        };
}
