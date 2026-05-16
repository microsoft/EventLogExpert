// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace EventLogExpert.Filtering;

/// <summary>Immutable representation of a single Basic-filter criterion (one row of the editor).</summary>
[JsonConverter(typeof(FilterComparisonJsonConverter))]
public sealed record FilterComparison
{
    public EventProperty Property { get; init; }

    public ComparisonOperator Operator { get; init; }

    public MatchMode MatchMode { get; init; }

    public string? Value { get; init; }

    public ImmutableList<string> Values { get; init; } = [];

    /// <summary>
    ///     Returns a copy with the new <paramref name="property" /> and Value/Values cleared, since the available value
    ///     space changes when the property changes.
    /// </summary>
    public FilterComparison WithProperty(EventProperty property) =>
        this with { Property = property, Value = null, Values = [] };
}
