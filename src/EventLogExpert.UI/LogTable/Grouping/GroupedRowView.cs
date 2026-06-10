// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using System.Collections;

namespace EventLogExpert.UI.LogTable.Grouping;

/// <summary>
///     Virtual row-view over an already group-sorted event list: header and event rows resolved on demand via O(log
///     groups) binary search over an <see cref="EventGroup" /> prefix-sum, with no per-row materialization.
/// </summary>
internal sealed class GroupedRowView : IReadOnlyList<TableRow>, IList<TableRow>
{
    private readonly IReadOnlyList<ResolvedEvent> _events;
    private readonly EventGroup[] _groups;

    private GroupedRowView(IReadOnlyList<ResolvedEvent> events, EventGroup[] groups, int count)
    {
        _events = events;
        _groups = groups;
        Count = count;
    }

    public int Count { get; }

    public IReadOnlyList<EventGroup> Groups => _groups;

    public bool IsReadOnly => true;

    public TableRow this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count) { throw new ArgumentOutOfRangeException(nameof(index)); }

            int groupIndex = FindGroupByVisibleRow(index);
            var group = _groups[groupIndex];

            return index == group.VisibleStart
                ? TableRow.ForHeader(groupIndex)
                : TableRow.ForEvent(group.StartIndex + (index - group.VisibleStart - 1), groupIndex);
        }
        set => throw new NotSupportedException();
    }

    public static GroupedRowView Build(
        IReadOnlyList<ResolvedEvent> events,
        ColumnName groupBy,
        Func<string, bool> isCollapsed)
    {
        var groups = new List<EventGroup>();
        int visible = 0;
        int index = 0;

        while (index < events.Count)
        {
            string key = ResolvedEventGroupKey.For(groupBy, events[index]);
            int start = index;
            index++;

            while (index < events.Count &&
                string.Equals(ResolvedEventGroupKey.For(groupBy, events[index]), key, StringComparison.Ordinal))
            {
                index++;
            }

            var group = new EventGroup(key, start, index - start, isCollapsed(key), visible);
            groups.Add(group);
            visible += group.VisibleSize;
        }

        return new GroupedRowView(events, [.. groups], visible);
    }

    public void Add(TableRow item) => throw new NotSupportedException();

    public void Clear() => throw new NotSupportedException();

    public bool Contains(TableRow item) => IndexOf(item) >= 0;

    public void CopyTo(TableRow[] array, int arrayIndex)
    {
        for (int i = 0; i < Count; i++) { array[arrayIndex + i] = this[i]; }
    }

    public ResolvedEvent EventAt(TableRow row) => _events[row.EventIndex];

    public IEnumerator<TableRow> GetEnumerator()
    {
        for (int i = 0; i < Count; i++) { yield return this[i]; }
    }

    public EventGroup GroupAt(TableRow row) => _groups[row.GroupIndex];

    public EventGroup GroupForEvent(int eventIndex) => _groups[FindGroupByEventIndex(eventIndex)];

    public int IndexOf(TableRow item)
    {
        for (int i = 0; i < Count; i++)
        {
            if (this[i] == item) { return i; }
        }

        return -1;
    }

    public void Insert(int index, TableRow item) => throw new NotSupportedException();

    public bool Remove(TableRow item) => throw new NotSupportedException();

    public void RemoveAt(int index) => throw new NotSupportedException();

    public bool TryGetGroupByKey(string key, out EventGroup group)
    {
        for (int i = 0; i < _groups.Length; i++)
        {
            if (string.Equals(_groups[i].Key, key, StringComparison.Ordinal))
            {
                group = _groups[i];

                return true;
            }
        }

        group = default;

        return false;
    }

    public int VisibleRowForEvent(int eventIndex)
    {
        var group = _groups[FindGroupByEventIndex(eventIndex)];

        return group.IsCollapsed
            ? group.VisibleStart
            : group.VisibleStart + 1 + (eventIndex - group.StartIndex);
    }

    public int VisibleRowForHeader(string groupKey)
    {
        for (int i = 0; i < _groups.Length; i++)
        {
            if (string.Equals(_groups[i].Key, groupKey, StringComparison.Ordinal)) { return _groups[i].VisibleStart; }
        }

        return -1;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private int FindGroup(int target, bool byVisibleStart)
    {
        int low = 0;
        int high = _groups.Length - 1;

        while (low < high)
        {
            int mid = low + ((high - low + 1) / 2);
            int midStart = byVisibleStart ? _groups[mid].VisibleStart : _groups[mid].StartIndex;

            if (midStart <= target) { low = mid; }
            else { high = mid - 1; }
        }

        return low;
    }

    private int FindGroupByEventIndex(int eventIndex) => FindGroup(eventIndex, byVisibleStart: false);

    private int FindGroupByVisibleRow(int visibleRow) => FindGroup(visibleRow, byVisibleStart: true);
}
