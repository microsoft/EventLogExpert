// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using System.Globalization;

namespace EventLogExpert.Runtime.Histogram;

public static class HistogramBuilder
{
    public static HistogramData? Build(
        IEventColumnView view,
        HistogramDimension dimension,
        int maxBuckets,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBuckets, 1);
        cancellationToken.ThrowIfCancellationRequested();

        if (!view.TryGetTimeTicksRange(out long minTicks, out long maxTicks, cancellationToken)) { return null; }

        long spanTicks = maxTicks - minTicks + 1;
        long bucketSpanTicks = Math.Max(1, (spanTicks + maxBuckets - 1) / maxBuckets);
        int bucketCount = (int)Math.Min((spanTicks + bucketSpanTicks - 1) / bucketSpanTicks, maxBuckets);

        (int[] slotCounts, int slotCount, IReadOnlyList<HistogramGroup> groups) = dimension switch
        {
            HistogramDimension.Severity => ScanSeverity(view, minTicks, bucketSpanTicks, bucketCount, cancellationToken),
            HistogramDimension.EventId => ScanByEventId(view, minTicks, bucketSpanTicks, bucketCount, cancellationToken),
            HistogramDimension.Log => ScanByField(view, EventFieldId.OwningLog, OwningLogDisplay.DistinctShortNames, minTicks, bucketSpanTicks, bucketCount, cancellationToken),
            _ => ScanByField(view, ToFieldId(dimension), static keys => keys, minTicks, bucketSpanTicks, bucketCount, cancellationToken)
        };

        int total = 0;

        foreach (int count in slotCounts) { total += count; }

        return new HistogramData(
            slotCounts,
            slotCount,
            bucketCount,
            new DateTime(minTicks, DateTimeKind.Utc),
            new DateTime(maxTicks, DateTimeKind.Utc),
            total,
            bucketSpanTicks,
            groups);
    }

    private static (int[] SlotCounts, int SlotCount, IReadOnlyList<HistogramGroup> Groups) ScanByEventId(
        IEventColumnView view,
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        CancellationToken cancellationToken)
    {
        var counts = new Dictionary<int, int>();
        view.CountEventIds(counts, cancellationToken);

        int[] targetIds = counts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key)
            .Take(HistogramConstants.MaxGroupByCategories)
            .Select(pair => pair.Key)
            .ToArray();

        int slotCount = targetIds.Length + 1;
        int[] slotCounts = new int[bucketCount * slotCount];
        view.BucketTimeTicksByEventId(minTicks, bucketSpanTicks, bucketCount, targetIds, slotCounts, cancellationToken);

        string[] keys = Array.ConvertAll(targetIds, id => id.ToString(CultureInfo.InvariantCulture));
        string[] labels = Array.ConvertAll(targetIds, id => id.ToString(CultureInfo.CurrentCulture));

        return (slotCounts, slotCount, HistogramGroups.ForCategories(keys, labels));
    }

    private static (int[] SlotCounts, int SlotCount, IReadOnlyList<HistogramGroup> Groups) ScanByField(
        IEventColumnView view,
        EventFieldId field,
        Func<IReadOnlyList<string>, IReadOnlyList<string>> labelMapper,
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        CancellationToken cancellationToken)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        view.CountFieldValues(field, counts, cancellationToken);

        string[] targetValues = counts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Take(HistogramConstants.MaxGroupByCategories)
            .Select(pair => pair.Key)
            .ToArray();

        int slotCount = targetValues.Length + 1;
        int[] slotCounts = new int[bucketCount * slotCount];
        view.BucketTimeTicksByField(minTicks, bucketSpanTicks, bucketCount, field, targetValues, slotCounts, cancellationToken);

        return (slotCounts, slotCount, HistogramGroups.ForCategories(targetValues, labelMapper(targetValues)));
    }

    private static (int[] SlotCounts, int SlotCount, IReadOnlyList<HistogramGroup> Groups) ScanSeverity(
        IEventColumnView view,
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        CancellationToken cancellationToken)
    {
        int slotCount = HistogramGroups.SeveritySlotCount;
        int[] slotCounts = new int[bucketCount * slotCount];
        view.BucketTimeTicksBySeverity(minTicks, bucketSpanTicks, bucketCount, slotCounts, cancellationToken);

        return (slotCounts, slotCount, HistogramGroups.Severity);
    }

    private static EventFieldId ToFieldId(HistogramDimension dimension) => dimension switch
    {
        HistogramDimension.Source => EventFieldId.Source,
        HistogramDimension.TaskCategory => EventFieldId.TaskCategory,
        HistogramDimension.Opcode => EventFieldId.Opcode,
        _ => throw new ArgumentOutOfRangeException(nameof(dimension), dimension, "Dimension is not a pooled string field.")
    };
}
