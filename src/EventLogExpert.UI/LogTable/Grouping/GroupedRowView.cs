// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using System.Collections;

namespace EventLogExpert.UI.LogTable.Grouping;

internal sealed class GroupedRowView : IReadOnlyList<TableRow>, IList<TableRow>
{
    private readonly Dictionary<string, int> _groupIndexByKey;
    private readonly EventGroup[] _groups;
    private readonly IEventColumnView _view;

    private GroupedRowView(
        IEventColumnView view,
        EventGroup[] groups,
        int count,
        Dictionary<string, int> groupIndexByKey)
    {
        _view = view;
        _groups = groups;
        Count = count;
        _groupIndexByKey = groupIndexByKey;
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
        IEventColumnView view,
        ColumnName groupBy,
        Func<string, bool> isCollapsed)
    {
        var groups = new List<EventGroup>();
        // Keys are unique per run, so a key-to-index map gives O(1) header lookup.
        var groupIndexByKey = new Dictionary<string, int>(StringComparer.Ordinal);
        int visible = 0;
        int start = 0;
        string? currentKey = null;

        // Walk the display order once, reading each row's group key column-direct (no rehydrate).
        for (int index = 0; index < view.Count; index++)
        {
            string key = view.GroupKeyAt(view.LocatorAt(index), groupBy);

            if (currentKey is null)
            {
                currentKey = key;
            }
            else if (!string.Equals(key, currentKey, StringComparison.Ordinal))
            {
                CloseGroup(groups, groupIndexByKey, currentKey, start, index, isCollapsed, ref visible);
                currentKey = key;
                start = index;
            }
        }

        if (currentKey is not null)
        {
            CloseGroup(groups, groupIndexByKey, currentKey, start, view.Count, isCollapsed, ref visible);
        }

        return new GroupedRowView(view, [.. groups], visible, groupIndexByKey);
    }

    public void Add(TableRow item) => throw new NotSupportedException();

    public void Clear() => throw new NotSupportedException();

    public bool Contains(TableRow item) => IndexOf(item) >= 0;

    public void CopyTo(TableRow[] array, int arrayIndex)
    {
        for (int i = 0; i < Count; i++) { array[arrayIndex + i] = this[i]; }
    }

    public DisplayRow EventAt(TableRow row)
    {
        var locator = _view.LocatorAt(row.EventIndex);

        return new DisplayRow(locator, _view.GetDetailLean(locator));
    }

    public EventLocator FirstLocatorOf(EventGroup group) => _view.LocatorAt(group.StartIndex);

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

    // The locator of the event row without rehydrating it, for cursor/nav that only needs identity.
    public EventLocator LocatorAt(TableRow row) => _view.LocatorAt(row.EventIndex);

    public bool Remove(TableRow item) => throw new NotSupportedException();

    public void RemoveAt(int index) => throw new NotSupportedException();

    public bool TryGetGroupByKey(string key, out EventGroup group)
    {
        if (_groupIndexByKey.TryGetValue(key, out int i))
        {
            group = _groups[i];

            return true;
        }

        group = default;

        return false;
    }

    public int VisibleRowForEvent(int eventIndex)
    {
        var group = _groups[FindGroupByEventIndex(eventIndex)];

        return group.IsCollapsed ?
            group.VisibleStart :
            group.VisibleStart + 1 + (eventIndex - group.StartIndex);
    }

    public int VisibleRowForHeader(string groupKey) =>
        _groupIndexByKey.TryGetValue(groupKey, out int i) ? _groups[i].VisibleStart : -1;

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private static void CloseGroup(
        List<EventGroup> groups,
        Dictionary<string, int> groupIndexByKey,
        string key,
        int start,
        int end,
        Func<string, bool> isCollapsed,
        ref int visible)
    {
        var group = new EventGroup(key, start, end - start, isCollapsed(key), visible);
        groupIndexByKey[key] = groups.Count;
        groups.Add(group);
        visible += group.VisibleSize;
    }

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
