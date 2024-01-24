// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using EventLogExpert.UI.Models;
using System.Collections.Immutable;
using System.Linq.Dynamic.Core;
using System.Text;

namespace EventLogExpert.UI;

public static class FilterMethods
{
    public static IDictionary<string, IEnumerable<DisplayEventModel>> FilterActiveLogs(
        ImmutableDictionary<string, EventLogData> activeLogs,
        EventFilter eventFilter)
    {
        Dictionary<string, IEnumerable<DisplayEventModel>> activeLogsFiltered = [];

        foreach (var activeLog in activeLogs)
        {
            activeLogsFiltered.Add(activeLog.Key, GetFilteredEvents(activeLog.Value.Events, eventFilter));
        }

        return activeLogsFiltered;
    }

    public static IEnumerable<DisplayEventModel> GetFilteredEvents(IEnumerable<DisplayEventModel> events, EventFilter eventFilter)
    {
        if (!IsFilteringEnabled(eventFilter)) { return events; }

        List<Func<DisplayEventModel, bool>> filters = [];

        if (eventFilter.DateFilter?.IsEnabled is true)
        {
            filters.Add(e =>
                e.TimeCreated >= eventFilter.DateFilter.After &&
                e.TimeCreated <= eventFilter.DateFilter.Before);
        }

        if (!eventFilter.AdvancedFilters.IsEmpty)
        {
            filters.Add(e => eventFilter.AdvancedFilters
                .Any(filter => filter.Comparison.Expression(e)));
        }

        if (!eventFilter.BasicFilters.IsEmpty)
        {
            filters.Add(e => eventFilter.BasicFilters
                .Any(filter => filter.Comparison.Expression(e)));
        }

        if (!eventFilter.CachedFilters.IsEmpty)
        {
            filters.Add(e => eventFilter.CachedFilters
                .Any(filter => filter.Comparison.Expression(e)));
        }

        return events.AsParallel()
            .Where(e => filters
                .Any(filter => filter(e)));
    }

    public static bool HasFilteringChanged(EventFilter updated, EventFilter original) =>
        updated.DateFilter?.Equals(original.DateFilter) is false ||
        updated.AdvancedFilters.Equals(original.AdvancedFilters) is false ||
        updated.BasicFilters.Equals(original.BasicFilters) is false ||
        updated.CachedFilters.Equals(original.CachedFilters) is false;

    public static bool IsFilteringEnabled(EventFilter eventFilter) =>
        eventFilter.DateFilter?.IsEnabled is true ||
        eventFilter.AdvancedFilters.IsEmpty is false ||
        eventFilter.BasicFilters.IsEmpty is false ||
        eventFilter.CachedFilters.IsEmpty is false;

    /// <summary>Sorts events by RecordId if no order is specified</summary>
    public static IEnumerable<DisplayEventModel> SortEvents(
        this IEnumerable<DisplayEventModel> events,
        ColumnName? orderBy = null,
        bool isDescending = false) => orderBy switch
    {
        ColumnName.Level => isDescending ? events.OrderByDescending(e => e.Level) : events.OrderBy(e => e.Level),
        ColumnName.DateAndTime => isDescending ?
            events.OrderByDescending(e => e.TimeCreated) :
            events.OrderBy(e => e.TimeCreated),
        ColumnName.ActivityId => isDescending ?
            events.OrderByDescending(e => e.ActivityId) :
            events.OrderBy(e => e.ActivityId),
        ColumnName.LogName => isDescending ? events.OrderByDescending(e => e.LogName) : events.OrderBy(e => e.LogName),
        ColumnName.ComputerName => isDescending ?
            events.OrderByDescending(e => e.ComputerName) :
            events.OrderBy(e => e.ComputerName),
        ColumnName.Source => isDescending ? events.OrderByDescending(e => e.Source) : events.OrderBy(e => e.Source),
        ColumnName.EventId => isDescending ? events.OrderByDescending(e => e.Id) : events.OrderBy(e => e.Id),
        ColumnName.TaskCategory => isDescending ?
            events.OrderByDescending(e => e.TaskCategory) :
            events.OrderBy(e => e.TaskCategory),
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
            filterModel.Data.Type is FilterType.KeywordsDisplayNames)
        {
            stringBuilder.Append(GetComparisonString(filterModel.Data.Type, filterModel.Data.Evaluator));
        }

        switch (filterModel.Data.Evaluator)
        {
            case FilterEvaluator.Equals :
            case FilterEvaluator.NotEqual :
                if (filterModel.Data.Type is FilterType.KeywordsDisplayNames)
                {
                    stringBuilder.Append($"\"{filterModel.Data.Value}\", StringComparison.OrdinalIgnoreCase))");
                }
                else
                {
                    stringBuilder.Append($"\"{filterModel.Data.Value}\"");
                }

                break;
            case FilterEvaluator.Contains :
            case FilterEvaluator.NotContains :
                if (filterModel.Data.Type is FilterType.KeywordsDisplayNames)
                {
                    stringBuilder.Append($"(\"{filterModel.Data.Value}\", StringComparison.OrdinalIgnoreCase))");
                }
                else
                {
                    stringBuilder.Append($"(\"{filterModel.Data.Value}\", StringComparison.OrdinalIgnoreCase)");
                }

                break;
            case FilterEvaluator.MultiSelect :
                if (filterModel.Data.Type is FilterType.KeywordsDisplayNames)
                {
                    stringBuilder.Append($"(e => (new[] {{\"{string.Join("\", \"", filterModel.Data.Values)}\"}}).Contains(e))");
                }
                else
                {
                    stringBuilder.Append($"(new[] {{\"{string.Join("\", \"", filterModel.Data.Values)}\"}}).Contains(");
                }

                break;
            default : return false;
        }

        if (filterModel.Data is { Evaluator: FilterEvaluator.MultiSelect, Type: not FilterType.KeywordsDisplayNames })
        {
            stringBuilder.Append(GetComparisonString(filterModel.Data.Type, filterModel.Data.Evaluator));
        }

        if (filterModel.SubFilters?.Count > 0)
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

    private static string GetComparisonString(FilterType type, FilterEvaluator evaluator) => evaluator switch
    {
        FilterEvaluator.Equals => type is FilterType.KeywordsDisplayNames ?
            $"{type}.Any(e => string.Equals(e, " :
            $"{type} == ",
        FilterEvaluator.Contains => type switch
        {
            FilterType.Id or FilterType.ActivityId => $"{type}.ToString().Contains",
            FilterType.KeywordsDisplayNames => $"{type}.Any(e => e.Contains",
            _ => $"{type}.Contains"
        },
        FilterEvaluator.NotEqual => type is FilterType.KeywordsDisplayNames ?
            $"!{type}.Any(e => string.Equals(e, " :
            $"{type} != ",
        FilterEvaluator.NotContains => type switch
        {
            FilterType.Id or FilterType.ActivityId => $"!{type}.ToString().Contains",
            FilterType.KeywordsDisplayNames => $"!{type}.Any(e => e.Contains",
            _ => $"!{type}.Contains"
        },
        FilterEvaluator.MultiSelect => type switch
        {
            FilterType.Id or FilterType.Level => $"{type}.ToString())",
            FilterType.KeywordsDisplayNames => $"{type}.Any",
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
            subFilter.Data.Type is FilterType.KeywordsDisplayNames)
        {
            stringBuilder.Append(GetComparisonString(subFilter.Data.Type, subFilter.Data.Evaluator));
        }

        switch (subFilter.Data.Evaluator)
        {
            case FilterEvaluator.Equals :
            case FilterEvaluator.NotEqual :
                if (subFilter.Data.Type is FilterType.KeywordsDisplayNames)
                {
                    stringBuilder.Append($"\"{subFilter.Data.Value}\", StringComparison.OrdinalIgnoreCase))");
                }
                else
                {
                    stringBuilder.Append($"\"{subFilter.Data.Value}\"");
                }

                break;
            case FilterEvaluator.Contains :
            case FilterEvaluator.NotContains :
                if (subFilter.Data.Type is FilterType.KeywordsDisplayNames)
                {
                    stringBuilder.Append($"(\"{subFilter.Data.Value}\", StringComparison.OrdinalIgnoreCase))");
                }
                else
                {
                    stringBuilder.Append($"(\"{subFilter.Data.Value}\", StringComparison.OrdinalIgnoreCase)");
                }

                break;
            case FilterEvaluator.MultiSelect :
                if (subFilter.Data.Type is FilterType.KeywordsDisplayNames)
                {
                    stringBuilder.Append($"(e => (new[] {{\"{string.Join("\", \"", subFilter.Data.Values)}\"}}).Contains(e))");
                }
                else
                {
                    stringBuilder.Append($"(new[] {{\"{string.Join("\", \"", subFilter.Data.Values)}\"}}).Contains(");
                }

                break;
            default : return null;
        }

        if (subFilter.Data is { Evaluator: FilterEvaluator.MultiSelect, Type: not FilterType.KeywordsDisplayNames })
        {
            stringBuilder.Append(GetComparisonString(subFilter.Data.Type, subFilter.Data.Evaluator));
        }

        return stringBuilder.ToString();
    }
}
