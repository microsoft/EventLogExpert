// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Helpers;
using EventLogExpert.Library.Models;
using EventLogExpert.Store.FilterPane;
using Microsoft.AspNetCore.Components;
using System.Text;

namespace EventLogExpert.Shared.Components;

public partial class FilterRow
{
    [Parameter] public FilterModel Value { get; set; } = null!;

    private static string GetComparisonString(FilterType type, FilterComparison comparison) => comparison switch
    {
        FilterComparison.Equals => $"{type} == ",
        FilterComparison.Contains => $"{type}.Contains",
        FilterComparison.NotEqual => $"{type} != ",
        FilterComparison.NotContains => $"!{type}.Contains",
        _ => string.Empty,
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

    private void AddSubFilter() => Dispatcher.Dispatch(new FilterPaneAction.AddSubFilter(Value.Id));

    private void EditFilter() => Value.IsEditing = true;

    private void RemoveFilter()
    {
        Dispatcher.Dispatch(new FilterPaneAction.RemoveFilter(Value));
        SaveFilter();
    }

    private void SaveFilter()
    {
        // Reset the filter since we are going to rebuild
        Value.Comparison.Clear();

        // Validate the filter and create the UI string
        if (SetComparisonString())
        {
            // Set the actual filter
            SetComparison(Value);

            if (Value.SubFilters.Count > 0)
            {
                foreach (var subFilter in Value.SubFilters)
                {
                    SetComparison(subFilterModel: subFilter);
                }
            }
        }

        Value.IsEditing = false;

        Dispatcher.Dispatch(new FilterPaneAction.ApplyFilters());
    }

    private void SetComparison(FilterModel? filterModel = null, SubFilterModel? subFilterModel = null)
    {
        if (filterModel is not null)
        {
            switch (filterModel.FilterComparison)
            {
                case FilterComparison.Equals :
                    SetEqualsComparison(filterModel.FilterType, filterModel.FilterValue);
                    break;
                case FilterComparison.Contains :
                    SetContainsComparison(filterModel.FilterType, filterModel.FilterValue);
                    break;
                case FilterComparison.NotEqual :
                    SetNotEqualsComparison(filterModel.FilterType, filterModel.FilterValue);
                    break;
                case FilterComparison.NotContains :
                    SetNotContainsComparison(filterModel.FilterType, filterModel.FilterValue);
                    break;
                default :
                    return;
            }
        }
        else if (subFilterModel is not null)
        {
            switch (subFilterModel.FilterComparison)
            {
                case FilterComparison.Equals :
                    SetEqualsComparison(subFilterModel.FilterType, subFilterModel.FilterValue);
                    break;
                case FilterComparison.Contains :
                    SetContainsComparison(subFilterModel.FilterType, subFilterModel.FilterValue);
                    break;
                case FilterComparison.NotEqual :
                    SetNotEqualsComparison(subFilterModel.FilterType, subFilterModel.FilterValue);
                    break;
                case FilterComparison.NotContains :
                    SetNotContainsComparison(subFilterModel.FilterType, subFilterModel.FilterValue);
                    break;
                default :
                    return;
            }
        }
    }

    private bool SetComparisonString()
    {
        if (string.IsNullOrWhiteSpace(Value.FilterValue))
        {
            Value.ComparisonString = null;
            return false;
        }

        StringBuilder stringBuilder = new(GetComparisonString(Value.FilterType, Value.FilterComparison));

        switch (Value.FilterComparison)
        {
            case FilterComparison.Equals :
            case FilterComparison.NotEqual :
                stringBuilder.Append($"\"{Value.FilterValue}\"");
                break;
            case FilterComparison.Contains :
            case FilterComparison.NotContains :
                stringBuilder.Append($"(\"{Value.FilterValue}\")");
                break;
            default : return false;
        }

        if (Value.SubFilters.Count > 0)
        {
            foreach (var subFilter in Value.SubFilters)
            {
                var comparison = GetSubFilterComparisonString(subFilter);

                if (comparison != null) { stringBuilder.AppendLine(comparison); }
            }
        }

        Value.ComparisonString = stringBuilder.ToString();

        return true;
    }

    private void SetContainsComparison(FilterType filterType, string value)
    {
        switch (filterType)
        {
            case FilterType.EventId :
                Value.Comparison.Add(x => x.Id.ToString().Contains(value));
                break;
            case FilterType.Level :
                Value.Comparison.Add(x => x.Level.ToString()?.Contains(value) is true);
                break;
            case FilterType.Source :
                Value.Comparison.Add(x =>
                    x.Source.Contains(value, StringComparison.InvariantCultureIgnoreCase));

                break;
            case FilterType.Task :
                Value.Comparison.Add(x =>
                    x.TaskCategory.Contains(value, StringComparison.InvariantCultureIgnoreCase));

                break;
            case FilterType.Description :
                Value.Comparison.Add(x =>
                    x.Description.Contains(value, StringComparison.InvariantCultureIgnoreCase));

                break;
            default :
                return;
        }
    }

    private void SetEqualsComparison(FilterType filterType, string value)
    {
        switch (filterType)
        {
            case FilterType.EventId :
                if (int.TryParse(value, out int id)) { Value.Comparison.Add(x => x.Id == id); }

                break;
            case FilterType.Level :
                if (Enum.TryParse(value, out SeverityLevel level)) { Value.Comparison.Add(x => x.Level == level); }

                break;
            case FilterType.Source :
                Value.Comparison.Add(x =>
                    string.Equals(x.Source, value, StringComparison.InvariantCultureIgnoreCase));

                break;
            case FilterType.Task :
                Value.Comparison.Add(x =>
                    string.Equals(x.TaskCategory, value, StringComparison.InvariantCultureIgnoreCase));

                break;
            case FilterType.Description :
                Value.Comparison.Add(x =>
                    string.Equals(x.Description, value, StringComparison.InvariantCultureIgnoreCase));

                break;
            default :
                return;
        }
    }

    private void SetNotContainsComparison(FilterType filterType, string value)
    {
        switch (filterType)
        {
            case FilterType.EventId :
                Value.Comparison.Add(x => !x.Id.ToString().Contains(value));
                break;
            case FilterType.Level :
                Value.Comparison.Add(x => !x.Level.ToString()?.Contains(value) is true);
                break;
            case FilterType.Source :
                Value.Comparison.Add(x =>
                    !x.Source.Contains(value, StringComparison.InvariantCultureIgnoreCase));

                break;
            case FilterType.Task :
                Value.Comparison.Add(x =>
                    !x.TaskCategory.Contains(value, StringComparison.InvariantCultureIgnoreCase));

                break;
            case FilterType.Description :
                Value.Comparison.Add(x =>
                    !x.Description.Contains(value, StringComparison.InvariantCultureIgnoreCase));

                break;
            default :
                return;
        }
    }

    private void SetNotEqualsComparison(FilterType filterType, string value)
    {
        switch (filterType)
        {
            case FilterType.EventId :
                if (int.TryParse(value, out int id)) { Value.Comparison.Add(x => x.Id != id); }

                break;
            case FilterType.Level :
                if (Enum.TryParse(value, out SeverityLevel level)) { Value.Comparison.Add(x => x.Level != level); }

                break;
            case FilterType.Source :
                Value.Comparison.Add(x =>
                    !string.Equals(x.Source, value, StringComparison.InvariantCultureIgnoreCase));

                break;
            case FilterType.Task :
                Value.Comparison.Add(x =>
                    !string.Equals(x.TaskCategory, value, StringComparison.InvariantCultureIgnoreCase));

                break;
            case FilterType.Description :
                Value.Comparison.Add(x =>
                    !string.Equals(x.Description, value, StringComparison.InvariantCultureIgnoreCase));

                break;
            default :
                return;
        }
    }

    private void ToggleFilter()
    {
        Dispatcher.Dispatch(new FilterPaneAction.ToggleFilter(Value.Id));
        Dispatcher.Dispatch(new FilterPaneAction.ApplyFilters());
    }
}
