// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.FilterPane;
using Fluxor;
using System.Linq.Dynamic.Core;
using System.Text;

namespace EventLogExpert.UI.Services;

public sealed class FilterService(IState<FilterPaneState> filterPaneState) : IFilterService
{
    private readonly IState<FilterPaneState> _filterPaneState = filterPaneState;

    public bool IsXmlEnabled => _filterPaneState.Value.IsXmlEnabled;

    public IDictionary<EventLogId, IEnumerable<DisplayEventModel>> FilterActiveLogs(
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

    public IEnumerable<DisplayEventModel> GetFilteredEvents(
        IEnumerable<DisplayEventModel> events,
        EventFilter eventFilter)
    {
        if (!FilterMethods.IsFilteringEnabled(eventFilter)) { return events; }

        return events.AsParallel()
            .Where(e => e.FilterByDate(eventFilter.DateFilter)
                .Filter(eventFilter.Filters, IsXmlEnabled));
    }

    public bool TryParse(FilterModel filterModel, out string comparison)
    {
        comparison = string.Empty;

        if (filterModel.Data.Category is FilterCategory.Xml && !IsXmlEnabled)
        {
            return false;
        }

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
                    stringBuilder.Append($"\"{filterModel.Data.Value?.Replace("\"", "\'")}\", StringComparison.OrdinalIgnoreCase))");
                }
                else
                {
                    stringBuilder.Append($"\"{filterModel.Data.Value?.Replace("\"", "\'")}\"");
                }

                break;
            case FilterEvaluator.Contains:
            case FilterEvaluator.NotContains:
                if (filterModel.Data.Category is FilterCategory.KeywordsDisplayNames)
                {
                    stringBuilder.Append($"(\"{filterModel.Data.Value?.Replace("\"", "\'")}\", StringComparison.OrdinalIgnoreCase))");
                }
                else
                {
                    stringBuilder.Append($"(\"{filterModel.Data.Value?.Replace("\"", "\'")}\", StringComparison.OrdinalIgnoreCase)");
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

    public bool TryParseExpression(string? expression, out string error, bool ignoreXml = false)
    {
        error = string.Empty;

        if (string.IsNullOrEmpty(expression)) { return false; }

        if (!IsXmlEnabled && (expression.Contains("Xml.", StringComparison.OrdinalIgnoreCase) && !ignoreXml))
        {
            error = "Xml filtering is not enabled";

            return false;
        }

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
                    stringBuilder.Append($"\"{subFilter.Data.Value?.Replace("\"", "\\\"")}\", StringComparison.OrdinalIgnoreCase))");
                }
                else
                {
                    stringBuilder.Append($"\"{subFilter.Data.Value?.Replace("\"", "\\\"")}\"");
                }

                break;
            case FilterEvaluator.Contains:
            case FilterEvaluator.NotContains:
                if (subFilter.Data.Category is FilterCategory.KeywordsDisplayNames)
                {
                    stringBuilder.Append($"(\"{subFilter.Data.Value?.Replace("\"", "\'")}\", StringComparison.OrdinalIgnoreCase))");
                }
                else
                {
                    stringBuilder.Append($"(\"{subFilter.Data.Value?.Replace("\"", "\'")}\", StringComparison.OrdinalIgnoreCase)");
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
