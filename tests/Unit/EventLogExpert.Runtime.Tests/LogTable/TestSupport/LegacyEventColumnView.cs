// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using System.Diagnostics.CodeAnalysis;

namespace EventLogExpert.Runtime.Tests.LogTable.TestSupport;

internal sealed class LegacyEventColumnView(
    EventLogId logId,
    int generation,
    long contentVersion,
    IReadOnlyList<ResolvedEvent> events) : IEventColumnView
{
    private readonly LegacyEventColumnReader _reader =
        new LegacyEventColumnReader(logId, generation, contentVersion, events);

    public int Count => _reader.Count;

    public IEventColumnReader Reader => _reader;

    public void BucketTimeTicksByEventData(long minTicks, long bucketSpanTicks, int bucketCount, string fieldName, long[] targetCodes, int[] slotCounts, CancellationToken cancellationToken) =>
        _reader.BucketTimeTicksByEventData(AllSurvive(), minTicks, bucketSpanTicks, bucketCount, fieldName, targetCodes, slotCounts, cancellationToken);

    public void BucketTimeTicksByEventId(long minTicks, long bucketSpanTicks, int bucketCount, int[] targetIds, int[] slotCounts, CancellationToken cancellationToken) =>
        _reader.BucketTimeTicksByEventId(AllSurvive(), minTicks, bucketSpanTicks, bucketCount, targetIds, slotCounts, cancellationToken);

    public void BucketTimeTicksByField(long minTicks, long bucketSpanTicks, int bucketCount, EventFieldId field, string[] targetValues, int[] slotCounts, CancellationToken cancellationToken) =>
        _reader.BucketTimeTicksByField(AllSurvive(), minTicks, bucketSpanTicks, bucketCount, field, targetValues, slotCounts, cancellationToken);

    public void BucketTimeTicksBySeverity(long minTicks, long bucketSpanTicks, int bucketCount, int[] slotCounts, CancellationToken cancellationToken) =>
        _reader.BucketTimeTicksBySeverity(AllSurvive(), minTicks, bucketSpanTicks, bucketCount, slotCounts, cancellationToken);

    public void CountEventDataValues(string fieldName, IDictionary<long, int> counts, CancellationToken cancellationToken) =>
        _reader.CountEventDataValues(AllSurvive(), fieldName, counts, cancellationToken);

    public void CountEventIds(IDictionary<int, int> counts, CancellationToken cancellationToken) =>
        _reader.CountEventIds(AllSurvive(), counts, cancellationToken);

    public void CountFieldValues(EventFieldId field, IDictionary<string, int> counts, CancellationToken cancellationToken) =>
        _reader.CountFieldValues(AllSurvive(), field, counts, cancellationToken);

    public IEnumerable<ResolvedEvent> EnumerateDetail()
    {
        for (int physical = 0; physical < _reader.Count; physical++)
        {
            yield return _reader.GetEvent(_reader.LocatorAt(physical));
        }
    }

    public ResolvedEvent GetDetail(EventLocator locator) => _reader.GetEvent(locator);

    public ResolvedEvent GetDetailLean(EventLocator locator) => _reader.GetDetailLean(locator);

    public string GroupKeyAt(EventLocator locator, ColumnName column) =>
        ResolvedEventGroupKey.For(_reader, locator, column);

    public EventLocator LocatorAt(int index) => _reader.LocatorAt(index);

    public int Rank(EventLocator locator) =>
        locator.LogId == _reader.LogId
        && locator.Generation == _reader.Generation
        && locator.Index >= 0
        && locator.Index < _reader.Count
            ? locator.Index
            : -1;

    public EventLocator? ResolveByKey(ValueKey key)
    {
        for (int physical = 0; physical < _reader.Count; physical++)
        {
            var locator = _reader.LocatorAt(physical);

            // First match wins; null-RecordId rows never produce a key, so they stay unresolvable, mirroring
            // EventColumnView.ResolveByKey so differential parity holds.
            if (ValueKey.TryCreate(_reader.GetDetailLean(locator), out ValueKey candidate) && candidate == key)
            {
                return locator;
            }
        }

        return null;
    }

    public IReadOnlyList<DisplayRow> Slice(int start, int count)
    {
        if (start < 0) { throw new ArgumentOutOfRangeException(nameof(start)); }

        if (count < 0) { throw new ArgumentOutOfRangeException(nameof(count)); }

        IReadOnlyList<ResolvedEvent> events = _reader.Events;
        int end = (int)Math.Min((long)start + count, events.Count);

        if (start >= end) { return []; }

        DisplayRow[] rows = new DisplayRow[end - start];

        for (int offset = 0; offset < rows.Length; offset++)
        {
            int physical = start + offset;
            rows[offset] = new DisplayRow(_reader.LocatorAt(physical), events[physical]);
        }

        return rows;
    }

    public bool TryGetDetail(EventLocator locator, [NotNullWhen(true)] out ResolvedEvent? detail)
    {
        if (locator.LogId == _reader.LogId
            && locator.Generation == _reader.Generation
            && locator.Index >= 0
            && locator.Index < _reader.Count)
        {
            detail = _reader.GetEvent(locator);

            return true;
        }

        detail = null;

        return false;
    }

    public bool TryGetTimeTicks(EventLocator locator, out long ticks)
    {
        if (locator.LogId == _reader.LogId
            && locator.Generation == _reader.Generation
            && locator.Index >= 0
            && locator.Index < _reader.Count)
        {
            ticks = _reader.GetEvent(locator).TimeCreated.Ticks;

            return true;
        }

        ticks = 0;

        return false;
    }

    public bool TryGetTimeTicksRange(out long minTicks, out long maxTicks, CancellationToken cancellationToken) =>
        _reader.TryGetTimeTicksRange(AllSurvive(), out minTicks, out maxTicks, cancellationToken);

    private int[] AllSurvive()
    {
        int[] ranks = new int[_reader.Count];

        for (int index = 0; index < ranks.Length; index++) { ranks[index] = index; }

        return ranks;
    }
}
