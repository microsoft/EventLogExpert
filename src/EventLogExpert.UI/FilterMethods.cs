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

    public static bool HasFilteringChanged(EventFilter updated, EventFilter original) =>
        !Equals(updated.DateFilter, original.DateFilter) ||
        !updated.Filters.Equals(original.Filters);

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

        return sorted;
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

    private static int WithTieBreaker(int primaryResult, DisplayEventModel a, DisplayEventModel b) =>
        primaryResult != 0 ? primaryResult : FallbackTieBreaker(Nullable.Compare(a.RecordId, b.RecordId), a, b);

    /// <summary>Falls back to RecordId, then OwningLog (for combined logs) to guarantee a total order for List.Sort stability.</summary>
    private static int FallbackTieBreaker(int recordIdResult, DisplayEventModel a, DisplayEventModel b) =>
        recordIdResult != 0 ? recordIdResult : string.Compare(a.OwningLog, b.OwningLog, StringComparison.Ordinal);

    extension(DisplayEventModel? @event)
    {
        public bool Filter(IEnumerable<FilterModel> filters, bool isXmlEnabled)
        {
            if (@event is null) { return false; }

            bool isEmpty = true;
            bool isFiltered = false;

            foreach (var filter in filters)
            {
                if (!isXmlEnabled && filter.Comparison.Value.Contains("xml.", StringComparison.OrdinalIgnoreCase)) { return false; }

                if (filter.IsExcluded && filter.Comparison.Expression(@event)) { return false; }

                if (!filter.IsExcluded) { isEmpty = false; }

                if (!filter.IsExcluded && filter.Comparison.Expression(@event)) { isFiltered = true; }
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
