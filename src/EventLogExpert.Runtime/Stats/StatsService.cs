// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using System.Globalization;

namespace EventLogExpert.Runtime.Stats;

internal sealed class StatsService : IStatsService
{
    public SeverityStats BuildSeverity(IEventColumnView view, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(view);

        int[] slots = new int[LevelSeverity.SlotCount];
        view.CountSeverity(slots, cancellationToken);

        return new SeverityStats(view.Count, slots);
    }

    public DimensionStats BuildDimension(
        IEventColumnView view,
        StatsDimension dimension,
        int topN,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topN);

        int total = view.Count;

        if (dimension == StatsDimension.EventId)
        {
            var counts = new Dictionary<int, int>();
            view.CountEventIds(counts, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            var topIds = counts
                .OrderByDescending(entry => entry.Value)
                .ThenBy(entry => entry.Key)
                .Take(topN)
                .Select(entry => new StatsContributor(entry.Key.ToString(CultureInfo.InvariantCulture), entry.Value))
                .ToList();

            // Every event carries an Id, so the counted total equals the filtered total and nothing is missing.
            return new DimensionStats
            {
                Dimension = dimension,
                Total = total,
                DistinctCount = counts.Count,
                MissingCount = total - counts.Values.Sum(),
                Top = topIds
            };
        }

        var fieldCounts = new Dictionary<string, int>();
        view.CountFieldValues(dimension.FieldId(), fieldCounts, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        var topValues = fieldCounts
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Take(topN)
            .Select(entry => new StatsContributor(entry.Key, entry.Value))
            .ToList();

        // CountFieldValues skips absent/empty values, so the counted total is the non-missing survivor count.
        return new DimensionStats
        {
            Dimension = dimension,
            Total = total,
            DistinctCount = fieldCounts.Count,
            MissingCount = total - fieldCounts.Values.Sum(),
            Top = topValues
        };
    }
}
