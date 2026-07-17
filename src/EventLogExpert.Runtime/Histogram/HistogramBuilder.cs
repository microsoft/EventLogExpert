// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.Common.Display;
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

        if (dimension is HistogramDimension.LogonType or HistogramDimension.TicketEncryptionType)
        {
            return BuildByEventData(view, ToEventDataFieldName(dimension), minTicks, maxTicks, bucketSpanTicks, bucketCount, cancellationToken);
        }

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

    private static HistogramData BuildByEventData(
        IEventColumnView view,
        string fieldName,
        long minTicks,
        long maxTicks,
        long bucketSpanTicks,
        int bucketCount,
        CancellationToken cancellationToken)
    {
        var counts = new Dictionary<long, int>();
        view.CountEventDataValues(fieldName, counts, cancellationToken);

        var minUtc = new DateTime(minTicks, DateTimeKind.Utc);
        var maxUtc = new DateTime(maxTicks, DateTimeKind.Utc);

        if (counts.Count == 0)
        {
            // No row in the view carries this field. Report the true survivor count (view.Count - every survivor falls within
            // the view's own min/max span) so the accessible region label isn't "0 events" over a non-empty span, and flag the
            // empty-state so the pane shows a message rather than a lone "Other" band.
            return new HistogramData([], 0, bucketCount, minUtc, maxUtc, view.Count, bucketSpanTicks, []) { GroupingFieldAbsent = true };
        }

        long[] targetCodes = counts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key)
            .Take(HistogramConstants.MaxGroupByCategories)
            .Select(pair => pair.Key)
            .ToArray();

        int slotCount = targetCodes.Length + 1;
        int[] slotCounts = new int[bucketCount * slotCount];
        view.BucketTimeTicksByEventData(minTicks, bucketSpanTicks, bucketCount, fieldName, targetCodes, slotCounts, cancellationToken);

        int total = 0;

        foreach (int count in slotCounts) { total += count; }

        // Key = the raw invariant code (stable toggle key); Label = the friendly decode, or the raw code when unrecognized.
        string[] keys = Array.ConvertAll(targetCodes, code => code.ToString(CultureInfo.InvariantCulture));
        string[] labels = Array.ConvertAll(
            targetCodes,
            code => EventDataValueDecoder.TryDecodeLabel(fieldName, code) ?? code.ToString(CultureInfo.InvariantCulture));

        return new HistogramData(
            slotCounts,
            slotCount,
            bucketCount,
            minUtc,
            maxUtc,
            total,
            bucketSpanTicks,
            HistogramGroups.ForCategories(keys, labels));
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

    private static string ToEventDataFieldName(HistogramDimension dimension) => dimension switch
    {
        HistogramDimension.LogonType => "LogonType",
        HistogramDimension.TicketEncryptionType => "TicketEncryptionType",
        _ => throw new ArgumentOutOfRangeException(nameof(dimension), dimension, "Dimension is not an EventData field.")
    };

    private static EventFieldId ToFieldId(HistogramDimension dimension) => dimension switch
    {
        HistogramDimension.Source => EventFieldId.Source,
        HistogramDimension.TaskCategory => EventFieldId.TaskCategory,
        HistogramDimension.Opcode => EventFieldId.Opcode,
        _ => throw new ArgumentOutOfRangeException(nameof(dimension), dimension, "Dimension is not a pooled string field.")
    };
}
