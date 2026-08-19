// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Stats;

/// <summary>
///     The severity composition of the filtered set: dense slots (0 = Unknown, 1-5 = Critical..Verbose) summing to
///     <see cref="Total" />.
/// </summary>
public readonly record struct SeverityStats(int Total, IReadOnlyList<int> Slots);

/// <summary>One ranked contributor of a dimension: its value and how many filtered events carry it.</summary>
public sealed record StatsContributor(string Value, int Count);

/// <summary>The top contributors of one dimension over the filtered set, plus the totals needed for shares and coverage.</summary>
public sealed record DimensionStats
{
    public required StatsDimension Dimension { get; init; }

    /// <summary>The filtered-set total (the denominator for every share); includes events with an absent/empty value.</summary>
    public required int Total { get; init; }

    /// <summary>The number of distinct non-missing values.</summary>
    public required int DistinctCount { get; init; }

    /// <summary>Events with an absent/empty value (string dimensions only; always 0 for Event ID).</summary>
    public required int MissingCount { get; init; }

    /// <summary>The ranked top contributors (count desc, deterministic tie-break), capped at the requested top-N.</summary>
    public required IReadOnlyList<StatsContributor> Top { get; init; }

    /// <summary>The number of events represented by the shown <see cref="Top" /> rows (for the coverage line).</summary>
    public int ShownEventCount => Top.Sum(contributor => contributor.Count);
}
