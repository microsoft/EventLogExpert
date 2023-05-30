﻿// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using System.Text;

namespace EventLogExpert.Library.Helpers;

public static class FilterMethods
{
    public static Func<DisplayEventModel, bool>? GetComparison(FilterComparison comparison,
        FilterType type,
        string value) => comparison switch
    {
        FilterComparison.Equals => GetEqualsComparison(type, value),
        FilterComparison.Contains => GetContainsComparison(type, value),
        FilterComparison.NotEqual => GetNotEqualsComparison(type, value),
        FilterComparison.NotContains => GetNotContainsComparison(type, value),
        _ => null
    };

    public static bool TryParse(FilterModel filterModel, out string? comparison)
    {
        comparison = null;

        if (string.IsNullOrWhiteSpace(filterModel.FilterValue)) { return false; }

        StringBuilder stringBuilder = new(GetComparisonString(filterModel.FilterType, filterModel.FilterComparison));

        switch (filterModel.FilterComparison)
        {
            case FilterComparison.Equals :
            case FilterComparison.NotEqual :
                stringBuilder.Append($"\"{filterModel.FilterValue}\"");
                break;
            case FilterComparison.Contains :
            case FilterComparison.NotContains :
                stringBuilder.Append($"(\"{filterModel.FilterValue}\")");
                break;
            default : return false;
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

    private static string GetComparisonString(FilterType type, FilterComparison comparison) => comparison switch
    {
        FilterComparison.Equals => $"{type} == ",
        FilterComparison.Contains => $"{type}.Contains",
        FilterComparison.NotEqual => $"{type} != ",
        FilterComparison.NotContains => $"!{type}.Contains",
        _ => string.Empty
    };

    private static Func<DisplayEventModel, bool>? GetContainsComparison(FilterType filterType, string value) =>
        filterType switch
        {
            FilterType.EventId => x => x.Id.ToString().Contains(value),
            FilterType.Level => x => x.Level.ToString()?.Contains(value) is true,
            FilterType.Source => x => x.Source.Contains(value, StringComparison.InvariantCultureIgnoreCase),
            FilterType.Task => x => x.TaskCategory.Contains(value, StringComparison.InvariantCultureIgnoreCase),
            FilterType.Description => x => x.Description.Contains(value, StringComparison.InvariantCultureIgnoreCase),
            _ => null
        };

    private static Func<DisplayEventModel, bool>? GetEqualsComparison(FilterType filterType, string value) =>
        filterType switch
        {
            FilterType.EventId => int.TryParse(value, out int id) ? x => x.Id == id : null,
            FilterType.Level => Enum.TryParse(value, out SeverityLevel level) ? x => x.Level == level : null,
            FilterType.Source => x => string.Equals(x.Source, value, StringComparison.InvariantCultureIgnoreCase),
            FilterType.Task => x => string.Equals(x.TaskCategory, value, StringComparison.InvariantCultureIgnoreCase),
            FilterType.Description => x =>
                string.Equals(x.Description, value, StringComparison.InvariantCultureIgnoreCase),
            _ => null
        };

    private static Func<DisplayEventModel, bool>? GetNotContainsComparison(FilterType filterType, string value) =>
        filterType switch
        {
            FilterType.EventId => x => !x.Id.ToString().Contains(value),
            FilterType.Level => x => !x.Level.ToString()?.Contains(value) is true,
            FilterType.Source => x => !x.Source.Contains(value, StringComparison.InvariantCultureIgnoreCase),
            FilterType.Task => x => !x.TaskCategory.Contains(value, StringComparison.InvariantCultureIgnoreCase),
            FilterType.Description => x => !x.Description.Contains(value, StringComparison.InvariantCultureIgnoreCase),
            _ => null
        };

    private static Func<DisplayEventModel, bool>? GetNotEqualsComparison(FilterType filterType, string value) =>
        filterType switch
        {
            FilterType.EventId => int.TryParse(value, out int id) ? x => x.Id != id : null,
            FilterType.Level => Enum.TryParse(value, out SeverityLevel level) ? x => x.Level != level : null,
            FilterType.Source => x => !string.Equals(x.Source, value, StringComparison.InvariantCultureIgnoreCase),
            FilterType.Task => x => !string.Equals(x.TaskCategory, value, StringComparison.InvariantCultureIgnoreCase),
            FilterType.Description => x =>
                !string.Equals(x.Description, value, StringComparison.InvariantCultureIgnoreCase),
            _ => null
        };

    private static string? GetSubFilterComparisonString(SubFilterModel subFilter)
    {
        if (string.IsNullOrWhiteSpace(subFilter.FilterValue)) { return null; }

        StringBuilder stringBuilder = new(" || ");
        stringBuilder.Append(GetComparisonString(subFilter.FilterType, subFilter.FilterComparison));

        switch (subFilter.FilterComparison)
        {
            case FilterComparison.Equals :
            case FilterComparison.NotEqual :
                stringBuilder.Append($"\"{subFilter.FilterValue}\"");
                break;
            case FilterComparison.Contains :
            case FilterComparison.NotContains :
                stringBuilder.Append($"(\"{subFilter.FilterValue}\")");
                break;
            default : return null;
        }

        return stringBuilder.ToString();
    }
}
