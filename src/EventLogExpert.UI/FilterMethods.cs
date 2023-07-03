// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.UI.Models;
using System.Text;
using static EventLogExpert.UI.Store.FilterPane.FilterPaneAction;

namespace EventLogExpert.UI;

public static class FilterMethods
{
    public static Func<DisplayEventModel, bool>? GetComparison(FilterComparison comparison,
        FilterType type,
        string value,
        List<string> values) => comparison switch
    {
        FilterComparison.Equals => GetEqualsComparison(type, value),
        FilterComparison.Contains => GetContainsComparison(type, value),
        FilterComparison.NotEqual => GetNotEqualsComparison(type, value),
        FilterComparison.NotContains => GetNotContainsComparison(type, value),
        FilterComparison.MultiSelect => GetMultiSelectComparison(type, values),
        _ => null
    };

    public static bool TryParse(FilterModel filterModel, out string? comparison)
    {
        comparison = null;

        if (string.IsNullOrWhiteSpace(filterModel.FilterValue) &&
            filterModel.FilterComparison != FilterComparison.MultiSelect) { return false; }

        if (!filterModel.FilterValues.Any() &&
            filterModel.FilterComparison == FilterComparison.MultiSelect) { return false; }

        StringBuilder stringBuilder = new();

        if (filterModel.FilterComparison != FilterComparison.MultiSelect ||
            filterModel.FilterType is FilterType.KeywordsDisplayNames)
        {
            stringBuilder.Append(GetComparisonString(filterModel.FilterType, filterModel.FilterComparison));
        }

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
            case FilterComparison.MultiSelect :
                if (filterModel.FilterType is FilterType.KeywordsDisplayNames)
                {
                    stringBuilder.Append($"(e => (new[] {{\"{string.Join("\", \"", filterModel.FilterValues)}\"}}).Contains(e))");
                }
                else
                {
                    stringBuilder.Append(
                        $"(new[] {{\"{string.Join("\", \"", filterModel.FilterValues)}\"}}).Contains(");
                }

                break;
            default : return false;
        }

        if (filterModel is { FilterComparison: FilterComparison.MultiSelect, FilterType: not FilterType.KeywordsDisplayNames })
        {
            stringBuilder.Append(GetComparisonString(filterModel.FilterType, filterModel.FilterComparison));
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
        FilterComparison.MultiSelect => type switch
        {
            FilterType.Id or FilterType.Level => $"{type}.ToString())",
            FilterType.KeywordsDisplayNames => $"{type}.Any",
            _ => $"{type})"
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

    private static Func<DisplayEventModel, bool>? GetMultiSelectComparison(FilterType filterType,
        ICollection<string> values) => filterType switch
    {
        FilterType.Id => x => values.Contains(x.Id.ToString()),
        FilterType.Level => x => values.Contains(x.Level?.ToString() ?? string.Empty),
        FilterType.KeywordsDisplayNames => x => x.KeywordsDisplayNames.Any(values.Contains),
        FilterType.Source => x => values.Contains(x.Source),
        FilterType.TaskCategory => x => values.Contains(x.TaskCategory),
        FilterType.Description => x => values.Contains(x.Description),
        _ => null
    };

    private static string? GetSubFilterComparisonString(SubFilterModel subFilter)
    {
        if (string.IsNullOrWhiteSpace(subFilter.FilterValue) &&
            subFilter.FilterComparison != FilterComparison.MultiSelect) { return null; }

        if (!subFilter.FilterValues.Any() &&
            subFilter.FilterComparison == FilterComparison.MultiSelect) { return null; }

        StringBuilder stringBuilder = new(" || ");

        if (subFilter.FilterComparison != FilterComparison.MultiSelect ||
            subFilter.FilterType is FilterType.KeywordsDisplayNames)
        {
            stringBuilder.Append(GetComparisonString(subFilter.FilterType, subFilter.FilterComparison));
        }

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
            case FilterComparison.MultiSelect :
                if (subFilter.FilterType is FilterType.KeywordsDisplayNames)
                {
                    stringBuilder.Append($"(e => (new[] {{\"{string.Join("\", \"", subFilter.FilterValues)}\"}}).Contains(e))");
                }
                else
                {
                    stringBuilder.Append($"(new[] {{\"{string.Join("\", \"", subFilter.FilterValues)}\"}}).Contains(");
                }

                break;
            default : return null;
        }

        if (subFilter is { FilterComparison: FilterComparison.MultiSelect, FilterType: not FilterType.KeywordsDisplayNames })
        {
            stringBuilder.Append(GetComparisonString(subFilter.FilterType, subFilter.FilterComparison));
        }

        return stringBuilder.ToString();
    }
}
