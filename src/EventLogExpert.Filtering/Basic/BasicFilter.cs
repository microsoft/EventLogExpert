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
}
