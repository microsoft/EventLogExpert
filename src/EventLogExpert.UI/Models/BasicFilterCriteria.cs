// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.UI.Models;

/// <summary>
///     Immutable representation of a single Basic-filter criterion (one row of the editor).
/// </summary>
public sealed record BasicFilterCriteria
{
    public FilterCategory Category { get; init; }

    public FilterEvaluator Evaluator { get; init; }

    public string? Value { get; init; }

    public ImmutableList<string> Values { get; init; } = [];

    /// <summary>
    ///     Returns a copy with the new <paramref name="category" /> and Value/Values cleared,
    ///     since the available value space changes when the category changes.
    /// </summary>
    public BasicFilterCriteria WithCategory(FilterCategory category) =>
        this with { Category = category, Value = null, Values = [] };
}
