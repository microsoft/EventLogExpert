// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using EventLogExpert.UI.Models;

namespace EventLogExpert.UI;

public static class FilterMethods
{
    public static Dictionary<string, FilterGroupData> AddFilterGroup(
        this Dictionary<string, FilterGroupData> group,
        string[] groupNames,
        FilterGroupModel data)
    {
        var root = groupNames.Length <= 1 ? string.Empty : groupNames.First();
        groupNames = groupNames.Skip(1).ToArray();

        if (group.TryGetValue(root, out var groupData))
        {
            if (groupNames.Length > 1)
            {
                groupData.ChildGroup.AddFilterGroup(groupNames, data);
            }
            else
            {
                groupData.FilterGroups.Add(data);
            }
        }
        else
        {
            group.Add(root,
                groupNames.Length > 1 ?
                    new FilterGroupData
                    {
                        ChildGroup = new Dictionary<string, FilterGroupData>()
                            .AddFilterGroup(groupNames, data)
                    } :
                    new FilterGroupData { FilterGroups = [data] });
        }

        return group;
    }

    public static bool HasFilteringChanged(EventFilter updated, EventFilter original)
    {
        if (!Equals(updated.DateFilter, original.DateFilter)) { return true; }

        var updatedSnapshots = updated.Snapshots;
        var originalSnapshots = original.Snapshots;

        // Default-constructed EventFilter (rare) has an uninitialized ImmutableArray.
        if (updatedSnapshots.IsDefault || originalSnapshots.IsDefault)
        {
            return updatedSnapshots.IsDefault != originalSnapshots.IsDefault;
        }

        if (updatedSnapshots.Length != originalSnapshots.Length) { return true; }

        for (int index = 0; index < updatedSnapshots.Length; index++)
        {
            if (!updatedSnapshots[index].Equals(originalSnapshots[index])) { return true; }
        }

        return false;
    }

    /// <summary>Returns the index of the specified item in the list, or -1 if not found.</summary>
    public static int IndexOf<T>(this IReadOnlyList<T> list, T item)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (EqualityComparer<T>.Default.Equals(list[i], item))
            {
                return i;
            }
        }

        return -1;
    }

    public static bool IsFilteringEnabled(EventFilter eventFilter) =>
        eventFilter.DateFilter?.IsEnabled is true ||
        eventFilter.Filters.IsEmpty is false;

    /// <summary>Sorts events by RecordId if no order is specified. Always returns a new list.</summary>
    public static IReadOnlyList<DisplayEventModel> SortEvents(
        this IEnumerable<DisplayEventModel> events,
        ColumnName? orderBy = null,
        bool isDescending = false)
    {
        var sorted = new List<DisplayEventModel>(events);
        sorted.Sort(GetComparer(orderBy, isDescending));

        return sorted.AsReadOnly();
    }

    internal static Comparison<DisplayEventModel> GetComparer(ColumnName? orderBy, bool isDescending)
    {
        // To reverse sort direction we swap operands (a,b)
        Comparison<DisplayEventModel> comparer = orderBy switch
        {
            ColumnName.Level => (a, b) => WithTieBreaker(string.Compare(a.Level, b.Level, StringComparison.Ordinal), a, b),
            ColumnName.DateAndTime => (a, b) => WithTieBreaker(a.TimeCreated.CompareTo(b.TimeCreated), a, b),
            ColumnName.ActivityId => (a, b) => WithTieBreaker(Nullable.Compare(a.ActivityId, b.ActivityId), a, b),
            ColumnName.Log => (a, b) => WithTieBreaker(string.Compare(a.LogName, b.LogName, StringComparison.Ordinal), a, b),
            ColumnName.ComputerName => (a, b) => WithTieBreaker(string.Compare(a.ComputerName, b.ComputerName, StringComparison.Ordinal), a, b),
            ColumnName.Source => (a, b) => WithTieBreaker(string.Compare(a.Source, b.Source, StringComparison.Ordinal), a, b),
            ColumnName.EventId => (a, b) => WithTieBreaker(a.Id.CompareTo(b.Id), a, b),
            ColumnName.TaskCategory => (a, b) => WithTieBreaker(string.Compare(a.TaskCategory, b.TaskCategory, StringComparison.Ordinal), a, b),
            ColumnName.Keywords => (a, b) => WithTieBreaker(string.Compare(a.KeywordsDisplayName, b.KeywordsDisplayName, StringComparison.Ordinal), a, b),
            ColumnName.ProcessId => (a, b) => WithTieBreaker(Nullable.Compare(a.ProcessId, b.ProcessId), a, b),
            ColumnName.ThreadId => (a, b) => WithTieBreaker(Nullable.Compare(a.ThreadId, b.ThreadId), a, b),
            ColumnName.User => (a, b) =>
            {
                var aStr = a.UserId?.Value;
                var bStr = b.UserId?.Value;
                return WithTieBreaker(string.Compare(aStr, bStr, StringComparison.Ordinal), a, b);
            },
            _ => (a, b) => FallbackTieBreaker(Nullable.Compare(a.RecordId, b.RecordId), a, b)
        };

        return isDescending ? (a, b) => comparer(b, a) : comparer;
    }

    internal static IReadOnlyList<DisplayEventModel> MergeSorted(
        IReadOnlyList<IReadOnlyList<DisplayEventModel>> sortedLists,
        ColumnName orderBy,
        bool isDescending)
    {
        switch (sortedLists.Count)
        {
            case 0: return [];
            case 1: return sortedLists[0];
        }

        int totalCount = 0;

        foreach (var list in sortedLists) { totalCount += list.Count; }

        if (totalCount == 0) { return []; }

        var comparer = GetComparer(orderBy, isDescending);
        var result = new List<DisplayEventModel>(totalCount);
        var heap = new PriorityQueue<int, DisplayEventModel>(
            sortedLists.Count,
            Comparer<DisplayEventModel>.Create(comparer));
        var positions = new int[sortedLists.Count];

        for (int listIndex = 0; listIndex < sortedLists.Count; listIndex++)
        {
            if (sortedLists[listIndex].Count <= 0) { continue; }

            heap.Enqueue(listIndex, sortedLists[listIndex][0]);
            positions[listIndex] = 1;
        }

        while (heap.TryDequeue(out int sourceListIndex, out DisplayEventModel? currentEvent))
        {
            result.Add(currentEvent);

            int nextPosition = positions[sourceListIndex];

            if (nextPosition >= sortedLists[sourceListIndex].Count) { continue; }

            heap.Enqueue(sourceListIndex, sortedLists[sourceListIndex][nextPosition]);
            positions[sourceListIndex] = nextPosition + 1;
        }

        return result.AsReadOnly();
    }


    internal static IReadOnlyList<DisplayEventModel> MergeSorted(
        IReadOnlyList<DisplayEventModel> existing,
        IReadOnlyList<DisplayEventModel> batch,
        ColumnName? orderBy,
        bool isDescending)
    {
        if (batch.Count == 0) { return existing; }

        if (existing.Count == 0) { return batch.SortEvents(orderBy, isDescending); }

        var comparer = GetComparer(orderBy, isDescending);

        var sortedBatch = new List<DisplayEventModel>(batch);
        sortedBatch.Sort(comparer);

        var result = new List<DisplayEventModel>(existing.Count + sortedBatch.Count);
        int i = 0, j = 0;

        while (i < existing.Count && j < sortedBatch.Count)
        {
            result.Add(comparer(existing[i], sortedBatch[j]) <= 0 ? existing[i++] : sortedBatch[j++]);
        }

        while (i < existing.Count) { result.Add(existing[i++]); }

        while (j < sortedBatch.Count) { result.Add(sortedBatch[j++]); }

        return result.AsReadOnly();
    }

    /// <summary>Falls back to RecordId, then OwningLog (for combined logs) to guarantee a total order for List.Sort stability.</summary>
    private static int FallbackTieBreaker(int recordIdResult, DisplayEventModel a, DisplayEventModel b) =>
        recordIdResult != 0 ? recordIdResult : string.Compare(a.OwningLog, b.OwningLog, StringComparison.Ordinal);

    private static int WithTieBreaker(int primaryResult, DisplayEventModel a, DisplayEventModel b) =>
        primaryResult != 0 ? primaryResult : FallbackTieBreaker(Nullable.Compare(a.RecordId, b.RecordId), a, b);

    extension(DisplayEventModel? @event)
    {
        public bool Filter(IEnumerable<FilterModel> filters)
        {
            if (@event is null) { return false; }

            bool isEmpty = true;
            bool isFiltered = false;

            foreach (var filter in filters)
            {
                if (filter.Compiled is null) { continue; }

                if (filter.IsExcluded && filter.Compiled.Predicate(@event)) { return false; }

                if (!filter.IsExcluded) { isEmpty = false; }

                if (!filter.IsExcluded && filter.Compiled.Predicate(@event)) { isFiltered = true; }
            }

            return isEmpty || isFiltered;
        }

        public DisplayEventModel? FilterByDate(FilterDateModel? dateFilter)
        {
            if (@event is null) { return null; }

            if (dateFilter is null) { return @event; }

            return @event.TimeCreated >= dateFilter.After && @event.TimeCreated <= dateFilter.Before ? @event : null;
        }
    }
}
