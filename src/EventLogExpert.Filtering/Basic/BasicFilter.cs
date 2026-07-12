// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace EventLogExpert.Filtering.Basic;

public sealed record BasicFilter(
    FilterComparison Comparison,
    [property: JsonPropertyName("SubFilters")] ImmutableList<FilterPredicate> Predicates)
{
    public static BasicFilter Empty { get; } = new(new FilterComparison(), []);

    /// <summary>
    ///     Returns a copy with <see cref="FilterComparison.WithNormalizedValues" /> applied to the root comparison and to
    ///     every predicate. Returns <see langword="this" /> unchanged when no comparison needed normalization.
    /// </summary>
    public BasicFilter WithNormalizedValues()
    {
        var comparison = Comparison.WithNormalizedValues();
        var predicates = Predicates;

        for (var index = 0; index < predicates.Count; index++)
        {
            var normalized = predicates[index].Comparison.WithNormalizedValues();

            if (!ReferenceEquals(normalized, predicates[index].Comparison))
            {
                predicates = predicates.SetItem(index, predicates[index] with { Comparison = normalized });
            }
        }

        return ReferenceEquals(comparison, Comparison) && ReferenceEquals(predicates, Predicates)
            ? this
            : new BasicFilter(Comparison: comparison, Predicates: predicates);
    }
}
