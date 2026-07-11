// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using System.Diagnostics.CodeAnalysis;

namespace EventLogExpert.Runtime.LogTable;

internal sealed class EventColumnView : IEventColumnView
{
    private readonly SortContext _context;
    private readonly int[] _order;
    private readonly int[] _rankByPhysical;
    private readonly IEventColumnReader _reader;

    private Dictionary<ValueKey, int>? _byKey;

    private EventColumnView(IEventColumnReader reader, int[] order, int[] rankByPhysical, SortContext context)
    {
        _reader = reader;
        _order = order;
        _rankByPhysical = rankByPhysical;
        _context = context;
    }

    public int Count => _order.Length;

    internal SortContext Context => _context;

    internal IEventColumnReader Reader => _reader;

    // The filter-surviving physical rows, used by WithContext to re-sort in place; the survivor set is order-independent,
    // so the current display order is a valid re-sort input.
    internal ReadOnlySpan<int> Survivors => _order;

    public IEnumerable<ResolvedEvent> EnumerateDetail()
    {
        foreach (int physical in _order)
        {
            yield return _reader.GetDetail(_reader.LocatorAt(physical));
        }
    }

    public ResolvedEvent GetDetail(EventLocator locator) => _reader.GetDetail(locator);

    public ResolvedEvent GetDetailLean(EventLocator locator) => _reader.GetDetailLean(locator);

    public string GroupKeyAt(EventLocator locator, ColumnName column) =>
        ResolvedEventGroupKey.For(_reader, locator, column);

    public EventLocator LocatorAt(int displayIndex) => _reader.LocatorAt(_order[displayIndex]);

    public int Rank(EventLocator locator) =>
        locator.LogId == _reader.LogId
        && locator.Generation == _reader.Generation
        && locator.Index >= 0
        && locator.Index < _rankByPhysical.Length
            ? _rankByPhysical[locator.Index]
            : -1;

    public EventLocator? ResolveByKey(ValueKey key)
    {
        var byKey = Volatile.Read(ref _byKey);

        if (byKey is null)
        {
            byKey = BuildByKey();

            // First writer wins; a racing reader either sees null (and builds its own discarded copy) or a complete map.
            byKey = Interlocked.CompareExchange(ref _byKey, byKey, null) ?? byKey;
        }

        return byKey.TryGetValue(key, out int physical) ? _reader.LocatorAt(physical) : null;
    }

    public IReadOnlyList<DisplayRow> Slice(int start, int count)
    {
        int clampedStart = Math.Clamp(start, 0, _order.Length);
        int clampedCount = Math.Clamp(count, 0, _order.Length - clampedStart);
        List<DisplayRow> rows = new(clampedCount);

        for (int offset = 0; offset < clampedCount; offset++)
        {
            EventLocator locator = LocatorAt(clampedStart + offset);
            rows.Add(new DisplayRow(locator, _reader.GetDetailLean(locator)));
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
            detail = _reader.GetDetail(locator);

            return true;
        }

        detail = null;

        return false;
    }

    /// <summary>
    ///     Builds a view over the filter-surviving <paramref name="survivors" />, sorted into display order, with the
    ///     physical-to-display inverse used by <see cref="Rank" />.
    /// </summary>
    internal static EventColumnView Create(
        IEventColumnReader reader,
        ReadOnlySpan<int> survivors,
        ColumnName? orderBy,
        bool isDescending,
        ColumnName? groupBy,
        bool isGroupDescending) =>
        Create(reader, survivors, new SortContext(orderBy, isDescending, groupBy, isGroupDescending));

    /// <summary>
    ///     Live-path overload: sorts under <paramref name="context" /> and retains it so the reducers can detect
    ///     (<see cref="HasContext" />) and repair (<see cref="WithContext" />) a stale sort without re-reading the raw store.
    /// </summary>
    internal static EventColumnView Create(IEventColumnReader reader, ReadOnlySpan<int> survivors, SortContext context)
    {
        ArgumentNullException.ThrowIfNull(reader);

        int[] order = ResolvedEventOrdering.SortColumnDirect(
            reader, survivors, context.OrderBy, context.IsDescending, context.GroupBy, context.IsGroupDescending);

        int[] rankByPhysical = new int[reader.Count];
        Array.Fill(rankByPhysical, -1);

        for (int display = 0; display < order.Length; display++)
        {
            rankByPhysical[order[display]] = display;
        }

        return new EventColumnView(reader, order, rankByPhysical, context);
    }

    internal bool HasContext(SortContext context) => _context.Equals(context);

    // Re-sorts the same survivor set under a new context without re-reading the raw store (the reducers use this when the
    // effective default order flips between single- and multi-log).
    internal EventColumnView WithContext(SortContext context) => Create(_reader, _order, context);

    private Dictionary<ValueKey, int> BuildByKey()
    {
        var map = new Dictionary<ValueKey, int>(_order.Length);

        foreach (int physical in _order)
        {
            // First survivor wins on a duplicate key; null-RecordId rows never produce a key, so they stay unresolvable.
            if (ValueKey.TryCreate(_reader.GetDetailLean(_reader.LocatorAt(physical)), out ValueKey key))
            {
                map.TryAdd(key, physical);
            }
        }

        return map;
    }
}
