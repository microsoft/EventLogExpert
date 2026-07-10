// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using System.Diagnostics.CodeAnalysis;

namespace EventLogExpert.Runtime.LogTable;

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
}
