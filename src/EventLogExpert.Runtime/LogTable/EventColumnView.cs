// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Runtime.LogTable;

/// <summary>
///     The real column-backed display view: pairs an <see cref="IEventColumnReader" /> with a sorted,
///     filter-surviving display order (<c>_order</c>, display -&gt; physical) and its inverse (<c>_rankByPhysical</c>,
///     physical -&gt; display, <c>-1</c> for a physical row not in the view). The viewport reads rows by display position
///     through <see cref="Slice" />; selection and highlight resolve by <see cref="EventLocator" /> through
///     <see cref="Rank" />. Additive and unwired; the live display path still runs through
///     <see cref="SegmentedSortedList" /> until the later flip.
/// </summary>
internal sealed class EventColumnView : IEventColumnView
{
    private readonly int[] _order;
    private readonly int[] _rankByPhysical;
    private readonly IEventColumnReader _reader;

    private EventColumnView(IEventColumnReader reader, int[] order, int[] rankByPhysical)
    {
        _reader = reader;
        _order = order;
        _rankByPhysical = rankByPhysical;
    }

    public int Count => _order.Length;

    public IEventColumnReader Reader => _reader;

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

    /// <summary>
    ///     Builds a view over <paramref name="survivors" /> (the filter-surviving physical rows) sorted into display
    ///     order by the <see cref="ResolvedEventOrdering.SortColumnDirect" /> kernel, then materializes the physical -&gt;
    ///     display inverse used by <see cref="Rank" />.
    /// </summary>
    internal static EventColumnView Create(
        IEventColumnReader reader,
        ReadOnlySpan<int> survivors,
        ColumnName? orderBy,
        bool isDescending,
        ColumnName? groupBy,
        bool isGroupDescending)
    {
        ArgumentNullException.ThrowIfNull(reader);

        int[] order = ResolvedEventOrdering.SortColumnDirect(
            reader, survivors, orderBy, isDescending, groupBy, isGroupDescending);

        int[] rankByPhysical = new int[reader.Count];
        Array.Fill(rankByPhysical, -1);

        for (int display = 0; display < order.Length; display++)
        {
            rankByPhysical[order[display]] = display;
        }

        return new EventColumnView(reader, order, rankByPhysical);
    }
}
