// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.UI.Models;
using System.Text;

namespace EventLogExpert.UI;

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
                if (filterModel.FilterType is FilterType.KeywordsDisplayNames)
                {
                    stringBuilder.Append($"\"{filterModel.FilterValue}\", StringComparison.OrdinalIgnoreCase))");
                }
                else
                {
                    stringBuilder.Append($"\"{filterModel.FilterValue}\"");
                }

                break;
            case FilterComparison.Contains :
            case FilterComparison.NotContains :
                if (filterModel.FilterType is FilterType.KeywordsDisplayNames)
                {
                    stringBuilder.Append($"(\"{filterModel.FilterValue}\", StringComparison.OrdinalIgnoreCase))");
                }
                else
                {
                    stringBuilder.Append($"(\"{filterModel.FilterValue}\", StringComparison.OrdinalIgnoreCase)");
                }

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
        FilterComparison.Equals => type is FilterType.KeywordsDisplayNames ?
            $"{type}.Any(e => string.Equals(e, " :
            $"{type} == ",
        FilterComparison.Contains => type switch
        {
            FilterType.Id or FilterType.Level => $"{type}.ToString().Contains",
            FilterType.KeywordsDisplayNames => $"{type}.Any(e => e.Contains",
            _ => $"{type}.Contains"
        },
        FilterComparison.NotEqual => type is FilterType.KeywordsDisplayNames ?
            $"!{type}.Any(e => string.Equals(e, " :
            $"{type} != ",
        FilterComparison.NotContains => type switch
        {
            FilterType.Id or FilterType.Level => $"!{type}.ToString().Contains",
            FilterType.KeywordsDisplayNames => $"!{type}.Any(e => e.Contains",
            _ => $"!{type}.Contains"
        },
        _ => string.Empty
    };

    private static Func<DisplayEventModel, bool>? GetContainsComparison(FilterType filterType, string value) =>
        filterType switch
        {
            FilterType.Id => x => x.Id.ToString().Contains(value),
            FilterType.Level => x => x.Level.ToString()?.Contains(value) is true,
            FilterType.KeywordsDisplayNames => x =>
                x.KeywordsDisplayNames.Any(e => e.Contains(value, StringComparison.OrdinalIgnoreCase)),
            FilterType.Source => x => x.Source.Contains(value, StringComparison.OrdinalIgnoreCase),
            FilterType.TaskCategory => x => x.TaskCategory.Contains(value, StringComparison.OrdinalIgnoreCase),
            FilterType.Description => x => x.Description.Contains(value, StringComparison.OrdinalIgnoreCase),
            _ => null
        };

    private static Func<DisplayEventModel, bool>? GetEqualsComparison(FilterType filterType, string value) =>
        filterType switch
        {
            FilterType.Id => int.TryParse(value, out int id) ? x => x.Id == id : null,
            FilterType.Level => Enum.TryParse(value, out SeverityLevel level) ? x => x.Level == level : null,
            FilterType.KeywordsDisplayNames => x =>
                x.KeywordsDisplayNames.Any(e => string.Equals(e, value, StringComparison.OrdinalIgnoreCase)),
            FilterType.Source => x => string.Equals(x.Source, value, StringComparison.OrdinalIgnoreCase),
            FilterType.TaskCategory => x => string.Equals(x.TaskCategory, value, StringComparison.OrdinalIgnoreCase),
            FilterType.Description => x =>
                string.Equals(x.Description, value, StringComparison.OrdinalIgnoreCase),
            _ => null
        };

    private static Func<DisplayEventModel, bool>? GetNotContainsComparison(FilterType filterType, string value) =>
        filterType switch
        {
            FilterType.Id => x => !x.Id.ToString().Contains(value),
            FilterType.Level => x => !x.Level.ToString()?.Contains(value) is true,
            FilterType.KeywordsDisplayNames => x =>
                !x.KeywordsDisplayNames.Any(e => e.Contains(value, StringComparison.OrdinalIgnoreCase)),
            FilterType.Source => x => !x.Source.Contains(value, StringComparison.OrdinalIgnoreCase),
            FilterType.TaskCategory => x => !x.TaskCategory.Contains(value, StringComparison.OrdinalIgnoreCase),
            FilterType.Description => x => !x.Description.Contains(value, StringComparison.OrdinalIgnoreCase),
            _ => null
        };

    private static Func<DisplayEventModel, bool>? GetNotEqualsComparison(FilterType filterType, string value) =>
        filterType switch
        {
            FilterType.Id => int.TryParse(value, out int id) ? x => x.Id != id : null,
            FilterType.Level => Enum.TryParse(value, out SeverityLevel level) ? x => x.Level != level : null,
            FilterType.KeywordsDisplayNames => x =>
                !x.KeywordsDisplayNames.Any(e => string.Equals(e, value, StringComparison.OrdinalIgnoreCase)),
            FilterType.Source => x => !string.Equals(x.Source, value, StringComparison.OrdinalIgnoreCase),
            FilterType.TaskCategory => x => !string.Equals(x.TaskCategory, value, StringComparison.OrdinalIgnoreCase),
            FilterType.Description => x =>
                !string.Equals(x.Description, value, StringComparison.OrdinalIgnoreCase),
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
                if (subFilter.FilterType is FilterType.KeywordsDisplayNames)
                {
                    stringBuilder.Append($"\"{subFilter.FilterValue}\", StringComparison.OrdinalIgnoreCase))");
                }
                else
                {
                    stringBuilder.Append($"\"{subFilter.FilterValue}\"");
                }

                break;
            case FilterComparison.Contains :
            case FilterComparison.NotContains :
                if (subFilter.FilterType is FilterType.KeywordsDisplayNames)
                {
                    stringBuilder.Append($"(\"{subFilter.FilterValue}\", StringComparison.OrdinalIgnoreCase))");
                }
                else
                {
                    stringBuilder.Append($"(\"{subFilter.FilterValue}\", StringComparison.OrdinalIgnoreCase)");
                }

                break;
            default : return null;
        }

        return stringBuilder.ToString();
    }
}
