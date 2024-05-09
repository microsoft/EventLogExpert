// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using EventLogExpert.UI.Models;
using System.Linq.Dynamic.Core;
using System.Text;

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
                    new FilterGroupData { ChildGroup = new Dictionary<string, FilterGroupData>().AddFilterGroup(groupNames, data) } :
                    new FilterGroupData { FilterGroups = [data] });
        }

        return group;
    }

    public static bool Filter(this DisplayEventModel? @event, IEnumerable<FilterModel> filters)
    {
        if (@event is null) { return false; }

        bool isEmpty = true;
        bool isFiltered = false;

        foreach (var filter in filters)
        {
            if (filter.IsExcluded && filter.Comparison.Expression(@event)) { return false; }

            if (!filter.IsExcluded) { isEmpty = false; }

            if (!filter.IsExcluded && filter.Comparison.Expression(@event)) { isFiltered = true; }
        }

        return isEmpty || isFiltered;
    }

    public static IDictionary<EventLogId, IEnumerable<DisplayEventModel>> FilterActiveLogs(
        IEnumerable<EventLogData> logData,
        EventFilter eventFilter)
    {
        Dictionary<EventLogId, IEnumerable<DisplayEventModel>> activeLogsFiltered = [];

        foreach (var data in logData)
        {
            activeLogsFiltered.Add(data.Id, GetFilteredEvents(data.Events, eventFilter));
        }

        return activeLogsFiltered;
    }

    public static DisplayEventModel? FilterByDate(this DisplayEventModel? @event, FilterDateModel? dateFilter)
    {
        if (@event is null) { return null; }

        if (dateFilter is null) { return @event; }

        return @event.TimeCreated >= dateFilter.After && @event.TimeCreated <= dateFilter.Before ? @event : null;
    }

    public static IEnumerable<DisplayEventModel> GetFilteredEvents(
        IEnumerable<DisplayEventModel> events,
        EventFilter eventFilter)
    {
        if (!IsFilteringEnabled(eventFilter)) { return events; }

        return events.AsParallel()
            .Where(e => e.FilterByDate(eventFilter.DateFilter)
                .Filter(eventFilter.Filters));
    }

    public static bool HasFilteringChanged(EventFilter updated, EventFilter original) =>
        updated.DateFilter?.Equals(original.DateFilter) is false ||
        updated.Filters.Equals(original.Filters) is false;

    public static bool IsFilteringEnabled(EventFilter eventFilter) =>
        eventFilter.DateFilter?.IsEnabled is true ||
        eventFilter.Filters.IsEmpty is false;

    /// <summary>Sorts events by RecordId if no order is specified</summary>
    public static IEnumerable<DisplayEventModel> SortEvents(
        this IEnumerable<DisplayEventModel> events,
        ColumnName? orderBy = null,
        bool isDescending = false) =>
        orderBy switch
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

    public static bool TryParse(FilterModel filterModel, out string comparison)
    {
        comparison = string.Empty;

        if (string.IsNullOrWhiteSpace(filterModel.Data.Value) &&
            filterModel.Data.Evaluator != FilterEvaluator.MultiSelect) { return false; }

        if (filterModel.Data.Values.Count <= 0 &&
            filterModel.Data.Evaluator == FilterEvaluator.MultiSelect) { return false; }

        StringBuilder stringBuilder = new();

        if (filterModel.Data.Evaluator != FilterEvaluator.MultiSelect ||
            filterModel.Data.Category is FilterCategory.KeywordsDisplayNames)
        {
            stringBuilder.Append(GetComparisonString(filterModel.Data.Category, filterModel.Data.Evaluator));
        }

        switch (filterModel.Data.Evaluator)
        {
            case FilterEvaluator.Equals:
            case FilterEvaluator.NotEqual:
                if (filterModel.Data.Category is FilterCategory.KeywordsDisplayNames)
                {
                    stringBuilder.Append($"\"{filterModel.Data.Value}\", StringComparison.OrdinalIgnoreCase))");
                }
                else
                {
                    stringBuilder.Append($"\"{filterModel.Data.Value}\"");
                }

                break;
            case FilterEvaluator.Contains:
            case FilterEvaluator.NotContains:
                if (filterModel.Data.Category is FilterCategory.KeywordsDisplayNames)
                {
                    stringBuilder.Append($"(\"{filterModel.Data.Value}\", StringComparison.OrdinalIgnoreCase))");
                }
                else
                {
                    stringBuilder.Append($"(\"{filterModel.Data.Value}\", StringComparison.OrdinalIgnoreCase)");
                }

                break;
            case FilterEvaluator.MultiSelect:
                if (filterModel.Data.Category is FilterCategory.KeywordsDisplayNames)
                {
                    stringBuilder.Append($"(e => (new[] {{\"{string.Join("\", \"", filterModel.Data.Values)}\"}}).Contains(e))");
                }
                else
                {
                    stringBuilder.Append($"(new[] {{\"{string.Join("\", \"", filterModel.Data.Values)}\"}}).Contains(");
                }

                break;
            default: return false;
        }

        if (filterModel.Data is { Evaluator: FilterEvaluator.MultiSelect, Category: not FilterCategory.KeywordsDisplayNames })
        {
            stringBuilder.Append(GetComparisonString(filterModel.Data.Category, filterModel.Data.Evaluator));
        }

        if (filterModel.SubFilters.Count > 0)
        {
            foreach (var subFilter in filterModel.SubFilters)
            {
                string? subFilterComparison = GetSubFilterComparisonString(subFilter);

                if (subFilterComparison != null) { stringBuilder.AppendLine(subFilterComparison); }
            }
        }

        comparison = stringBuilder.ToString();

        return true;
    }

    public static bool TryParseExpression(string? expression, out string error)
    {
        error = string.Empty;

        if (string.IsNullOrEmpty(expression)) { return false; }

        try
        {
            _ = Enumerable.Empty<DisplayEventModel>().AsQueryable()
                .Where(EventLogExpertCustomTypeProvider.ParsingConfig, expression);

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string GetComparisonString(FilterCategory type, FilterEvaluator evaluator) => evaluator switch
    {
        FilterEvaluator.Equals => type is FilterCategory.KeywordsDisplayNames ?
            $"{type}.Any(e => string.Equals(e, " :
            $"{type} == ",
        FilterEvaluator.Contains => type switch
        {
            FilterCategory.Id or FilterCategory.ActivityId => $"{type}.ToString().Contains",
            FilterCategory.KeywordsDisplayNames => $"{type}.Any(e => e.Contains",
            _ => $"{type}.Contains"
        },
        FilterEvaluator.NotEqual => type is FilterCategory.KeywordsDisplayNames ?
            $"!{type}.Any(e => string.Equals(e, " :
            $"{type} != ",
        FilterEvaluator.NotContains => type switch
        {
            FilterCategory.Id or FilterCategory.ActivityId => $"!{type}.ToString().Contains",
            FilterCategory.KeywordsDisplayNames => $"!{type}.Any(e => e.Contains",
            _ => $"!{type}.Contains"
        },
        FilterEvaluator.MultiSelect => type switch
        {
            FilterCategory.Id or FilterCategory.Level => $"{type}.ToString())",
            FilterCategory.KeywordsDisplayNames => $"{type}.Any",
            _ => $"{type})"
        },
        _ => string.Empty
    };

    private static string? GetSubFilterComparisonString(FilterModel subFilter)
    {
        if (string.IsNullOrWhiteSpace(subFilter.Data.Value) &&
            subFilter.Data.Evaluator != FilterEvaluator.MultiSelect) { return null; }

        if (subFilter.Data.Values.Count <= 0 &&
            subFilter.Data.Evaluator == FilterEvaluator.MultiSelect) { return null; }

        StringBuilder stringBuilder = new(subFilter.ShouldCompareAny ? " || " : " && ");

        if (subFilter.Data.Evaluator != FilterEvaluator.MultiSelect ||
            subFilter.Data.Category is FilterCategory.KeywordsDisplayNames)
        {
            stringBuilder.Append(GetComparisonString(subFilter.Data.Category, subFilter.Data.Evaluator));
        }

        switch (subFilter.Data.Evaluator)
        {
            case FilterEvaluator.Equals:
            case FilterEvaluator.NotEqual:
                if (subFilter.Data.Category is FilterCategory.KeywordsDisplayNames)
                {
                    stringBuilder.Append($"\"{subFilter.Data.Value}\", StringComparison.OrdinalIgnoreCase))");
                }
                else
                {
                    stringBuilder.Append($"\"{subFilter.Data.Value}\"");
                }

                break;
            case FilterEvaluator.Contains:
            case FilterEvaluator.NotContains:
                if (subFilter.Data.Category is FilterCategory.KeywordsDisplayNames)
                {
                    stringBuilder.Append($"(\"{subFilter.Data.Value}\", StringComparison.OrdinalIgnoreCase))");
                }
                else
                {
                    stringBuilder.Append($"(\"{subFilter.Data.Value}\", StringComparison.OrdinalIgnoreCase)");
                }

                break;
            case FilterEvaluator.MultiSelect:
                if (subFilter.Data.Category is FilterCategory.KeywordsDisplayNames)
                {
                    stringBuilder.Append($"(e => (new[] {{\"{string.Join("\", \"", subFilter.Data.Values)}\"}}).Contains(e))");
                }
                else
                {
                    stringBuilder.Append($"(new[] {{\"{string.Join("\", \"", subFilter.Data.Values)}\"}}).Contains(");
                }

                break;
            default: return null;
        }

        if (subFilter.Data is { Evaluator: FilterEvaluator.MultiSelect, Category: not FilterCategory.KeywordsDisplayNames })
        {
            stringBuilder.Append(GetComparisonString(subFilter.Data.Category, subFilter.Data.Evaluator));
        }

        return stringBuilder.ToString();
    }
}
