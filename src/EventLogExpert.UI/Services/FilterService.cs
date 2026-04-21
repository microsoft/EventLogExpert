// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using System.Linq.Dynamic.Core;
using System.Text;

namespace EventLogExpert.UI.Services;

public sealed class FilterService : IFilterService
{
    public IReadOnlyDictionary<EventLogId, IReadOnlyList<DisplayEventModel>> FilterActiveLogs(
        IEnumerable<EventLogData> logData,
        EventFilter eventFilter)
    {
        Dictionary<EventLogId, IReadOnlyList<DisplayEventModel>> activeLogsFiltered = [];

        foreach (var data in logData)
        {
            activeLogsFiltered.Add(data.Id, GetFilteredEvents(data.Events, eventFilter));
        }

        return activeLogsFiltered;
    }

    public IReadOnlyList<DisplayEventModel> GetFilteredEvents(
        IEnumerable<DisplayEventModel> events,
        EventFilter eventFilter)
    {
        if (!FilterMethods.IsFilteringEnabled(eventFilter))
        {
            return events as IReadOnlyList<DisplayEventModel> ?? [.. events];
        }

        // For small collections, PLINQ's thread scheduling overhead exceeds the
        // parallelism benefit. Use sequential filtering below the threshold.
        if (events is IReadOnlyCollection<DisplayEventModel> { Count: < 10_000 } collection)
        {
            return collection
                .Where(e => e.FilterByDate(eventFilter.DateFilter)
                    .Filter(eventFilter.Filters))
                .ToList()
                .AsReadOnly();
        }

        return events.AsParallel()
            .Where(e => e.FilterByDate(eventFilter.DateFilter)
                .Filter(eventFilter.Filters))
            .ToList()
            .AsReadOnly();
    }

    public bool TryParse(FilterModel filterModel, out string comparison)
    {
        comparison = string.Empty;

        if (string.IsNullOrWhiteSpace(filterModel.Data.Value) &&
            filterModel.Data.Evaluator != FilterEvaluator.MultiSelect) { return false; }

        if (filterModel.Data.Values.Count <= 0 &&
            filterModel.Data.Evaluator == FilterEvaluator.MultiSelect) { return false; }

        StringBuilder stringBuilder = new();

        if (filterModel.Data.Evaluator != FilterEvaluator.MultiSelect ||
            filterModel.Data.Category is FilterCategory.Keywords)
        {
            stringBuilder.Append(GetComparisonString(filterModel.Data.Category, filterModel.Data.Evaluator));
        }

        switch (filterModel.Data.Evaluator)
        {
            case FilterEvaluator.Equals:
            case FilterEvaluator.NotEqual:
                if (filterModel.Data.Category is FilterCategory.Keywords)
                {
                    stringBuilder.Append($"\"{filterModel.Data.Value?.Replace("\"", "\'")}\", StringComparison.OrdinalIgnoreCase))");
                }
                else
                {
                    stringBuilder.Append($"\"{filterModel.Data.Value?.Replace("\"", "\'")}\"");
                }

                break;
            case FilterEvaluator.Contains:
            case FilterEvaluator.NotContains:
                if (filterModel.Data.Category is FilterCategory.Keywords)
                {
                    stringBuilder.Append($"(\"{filterModel.Data.Value?.Replace("\"", "\'")}\", StringComparison.OrdinalIgnoreCase))");
                }
                else
                {
                    stringBuilder.Append($"(\"{filterModel.Data.Value?.Replace("\"", "\'")}\", StringComparison.OrdinalIgnoreCase)");
                }

                break;
            case FilterEvaluator.MultiSelect:
                if (filterModel.Data.Category is FilterCategory.Keywords)
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

        if (filterModel.Data is { Evaluator: FilterEvaluator.MultiSelect, Category: not FilterCategory.Keywords })
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

    public bool TryParseExpression(string? expression, out string error)
    {
        error = string.Empty;

        if (string.IsNullOrEmpty(expression)) { return false; }

        try
        {
            _ = Enumerable.Empty<DisplayEventModel>().AsQueryable()
                .Where(ParsingConfig.Default, expression);

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
        FilterEvaluator.Equals => type switch
        {
            FilterCategory.Keywords => $"{type}.Any(e => string.Equals(e, ",
            FilterCategory.UserId => $"{type} != null && {type}.Value == ",
            _ => $"{type} == "
        },
        FilterEvaluator.Contains => type switch
        {
            FilterCategory.Id or FilterCategory.ActivityId => $"{type}.ToString().Contains",
            FilterCategory.Keywords => $"{type}.Any(e => e.Contains",
            FilterCategory.UserId => $"{type} != null && {type}.Value.Contains",
            _ => $"{type}.Contains"
        },
        FilterEvaluator.NotEqual => type switch
        {
            FilterCategory.Keywords => $"!{type}.Any(e => string.Equals(e, ",
            FilterCategory.UserId => $"{type} != null && {type}.Value != ",
            _ => $"{type} != ",
        },
        FilterEvaluator.NotContains => type switch
        {
            FilterCategory.Id or FilterCategory.ActivityId => $"!{type}.ToString().Contains",
            FilterCategory.Keywords => $"!{type}.Any(e => e.Contains",
            FilterCategory.UserId => $"{type} != null && !{type}.Value.Contains",
            _ => $"!{type}.Contains"
        },
        FilterEvaluator.MultiSelect => type switch
        {
            FilterCategory.Id or FilterCategory.Level => $"{type}.ToString())",
            FilterCategory.Keywords => $"{type}.Any",
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
            subFilter.Data.Category is FilterCategory.Keywords)
        {
            stringBuilder.Append(GetComparisonString(subFilter.Data.Category, subFilter.Data.Evaluator));
        }

        switch (subFilter.Data.Evaluator)
        {
            case FilterEvaluator.Equals:
            case FilterEvaluator.NotEqual:
                if (subFilter.Data.Category is FilterCategory.Keywords)
                {
                    stringBuilder.Append($"\"{subFilter.Data.Value?.Replace("\"", "\\\"")}\", StringComparison.OrdinalIgnoreCase))");
                }
                else
                {
                    stringBuilder.Append($"\"{subFilter.Data.Value?.Replace("\"", "\\\"")}\"");
                }

                break;
            case FilterEvaluator.Contains:
            case FilterEvaluator.NotContains:
                if (subFilter.Data.Category is FilterCategory.Keywords)
                {
                    stringBuilder.Append($"(\"{subFilter.Data.Value?.Replace("\"", "\'")}\", StringComparison.OrdinalIgnoreCase))");
                }
                else
                {
                    stringBuilder.Append($"(\"{subFilter.Data.Value?.Replace("\"", "\'")}\", StringComparison.OrdinalIgnoreCase)");
                }

                break;
            case FilterEvaluator.MultiSelect:
                if (subFilter.Data.Category is FilterCategory.Keywords)
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

        if (subFilter.Data is { Evaluator: FilterEvaluator.MultiSelect, Category: not FilterCategory.Keywords })
        {
            stringBuilder.Append(GetComparisonString(subFilter.Data.Category, subFilter.Data.Evaluator));
        }

        return stringBuilder.ToString();
    }
}
