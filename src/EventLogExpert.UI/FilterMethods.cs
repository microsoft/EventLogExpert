// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using EventLogExpert.UI.Models;
using System.Collections.ObjectModel;

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

    public static bool Filter(this DisplayEventModel? @event, IEnumerable<FilterModel> filters, bool isXmlEnabled)
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

    public static DisplayEventModel? FilterByDate(this DisplayEventModel? @event, FilterDateModel? dateFilter)
    {
        if (@event is null) { return null; }

        if (dateFilter is null) { return @event; }

        return @event.TimeCreated >= dateFilter.After && @event.TimeCreated <= dateFilter.Before ? @event : null;
    }

    public static bool HasFilteringChanged(EventFilter updated, EventFilter original) =>
        updated.DateFilter?.Equals(original.DateFilter) is false ||
        updated.Filters.Equals(original.Filters) is false;

    public static bool IsFilteringEnabled(EventFilter eventFilter) =>
        eventFilter.DateFilter?.IsEnabled is true ||
        eventFilter.Filters.IsEmpty is false;

    /// <summary>Sorts events by RecordId if no order is specified</summary>
    public static ReadOnlyCollection<DisplayEventModel> SortEvents(
        this IEnumerable<DisplayEventModel> events,
        ColumnName? orderBy = null,
        bool isDescending = false)
    {
        var sortedEvents = orderBy switch
        {
            ColumnName.Level => isDescending ? events.OrderByDescending(e => e.Level) : events.OrderBy(e => e.Level),
            ColumnName.DateAndTime => isDescending ?
                events.OrderByDescending(e => e.TimeCreated) :
                events.OrderBy(e => e.TimeCreated),
            ColumnName.ActivityId => isDescending ?
                events.OrderByDescending(e => e.ActivityId) :
                events.OrderBy(e => e.ActivityId),
            ColumnName.Log => isDescending ? events.OrderByDescending(e => e.LogName) : events.OrderBy(e => e.LogName),
            ColumnName.ComputerName => isDescending ?
                events.OrderByDescending(e => e.ComputerName) :
                events.OrderBy(e => e.ComputerName),
            ColumnName.Source => isDescending ? events.OrderByDescending(e => e.Source) : events.OrderBy(e => e.Source),
            ColumnName.EventId => isDescending ? events.OrderByDescending(e => e.Id) : events.OrderBy(e => e.Id),
            ColumnName.TaskCategory => isDescending ?
                events.OrderByDescending(e => e.TaskCategory) :
                events.OrderBy(e => e.TaskCategory),
            ColumnName.Keywords => isDescending ?
                events.OrderByDescending(e => e.KeywordsDisplayNames) :
                events.OrderBy(e => e.KeywordsDisplayNames),
            ColumnName.ProcessId => isDescending ?
                events.OrderByDescending(e => e.ProcessId) :
                events.OrderBy(e => e.ProcessId),
            ColumnName.ThreadId => isDescending ?
                events.OrderByDescending(e => e.ThreadId) :
                events.OrderBy(e => e.ThreadId),
            ColumnName.User => isDescending ? events.OrderByDescending(e => e.UserId) : events.OrderBy(e => e.UserId),
            _ => isDescending ? events.OrderByDescending(e => e.RecordId) : events.OrderBy(e => e.RecordId)
        };

        return sortedEvents.ToList().AsReadOnly();
    }
}
