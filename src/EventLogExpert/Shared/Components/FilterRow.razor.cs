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

    private void AddSubFilter() => Dispatcher.Dispatch(new FilterPaneAction.AddSubFilter(Value.Id));

    private void EditFilter() => Value.IsEditing = true;

    private string? GetSubFilterComparisonString(SubFilterModel subFilter)
    {
        if (!IsValidFilterValue(subFilter.FilterValue))
        {
            return null;
        }

        StringBuilder stringBuilder = new(" OR ");

        switch (subFilter.FilterComparison)
        {
            case FilterComparison.Equals :
                stringBuilder.AppendLine("== ");
                break;
            case FilterComparison.Contains :
                stringBuilder.AppendLine("contains ");
                break;
            case FilterComparison.NotEqual :
                stringBuilder.AppendLine("!= ");
                break;
            case FilterComparison.NotContains :
                stringBuilder.AppendLine("!contains ");
                break;
            default :
                return null;
        }

        stringBuilder.AppendLine($"{subFilter.FilterValue}");
        return stringBuilder.ToString();
    }

    private bool IsValidFilterValue(dynamic value)
    {
        return Value.FilterType switch
        {
            FilterType.EventId or FilterType.Level => value < 0 is false,
            FilterType.Source or FilterType.Task or FilterType.Description => string.IsNullOrWhiteSpace(value) is false,
            _ => false,
        };
    }

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
                    SetEqualsComparison(filterModel.FilterValue);
                    break;
                case FilterComparison.Contains :
                    SetContainsComparison(filterModel.FilterValue);
                    break;
                case FilterComparison.NotEqual :
                    SetNotEqualsComparison(filterModel.FilterValue);
                    break;
                case FilterComparison.NotContains :
                    SetNotContainsComparison(filterModel.FilterValue);
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
                    SetEqualsComparison(subFilterModel.FilterValue);
                    break;
                case FilterComparison.Contains :
                    SetContainsComparison(subFilterModel.FilterValue);
                    break;
                case FilterComparison.NotEqual :
                    SetNotEqualsComparison(subFilterModel.FilterValue);
                    break;
                case FilterComparison.NotContains :
                    SetNotContainsComparison(subFilterModel.FilterValue);
                    break;
                default :
                    return;
            }
        }
    }

    private bool SetComparisonString()
    {
        if (!IsValidFilterValue(Value.FilterValue))
        {
            Value.ComparisonString = null;
            return false;
        }

        StringBuilder stringBuilder = new();

        stringBuilder.AppendLine($"{Value.FilterType} ");

        switch (Value.FilterComparison)
        {
            case FilterComparison.Equals :
                stringBuilder.AppendLine("== ");
                break;
            case FilterComparison.Contains :
                stringBuilder.AppendLine("contains ");
                break;
            case FilterComparison.NotEqual :
                stringBuilder.AppendLine("!= ");
                break;
            case FilterComparison.NotContains :
                stringBuilder.AppendLine("!contains ");
                break;
            default :
                return false;
        }

        stringBuilder.AppendLine($"{Value.FilterValue}");

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

    private void SetContainsComparison(dynamic? value)
    {
        switch (Value.FilterType)
        {
            case FilterType.EventId :
                Value.Comparison.Add(x => x.Id.ToString().Contains(value?.ToString()));
                break;
            case FilterType.Level :
                Value.Comparison.Add(x => x.Level.ToString()?.Contains(value?.ToString()));
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

    private void SetEqualsComparison(dynamic? value)
    {
        switch (Value.FilterType)
        {
            case FilterType.EventId :
                Value.Comparison.Add(x => x.Id == value);
                break;
            case FilterType.Level :
                Value.Comparison.Add(x => x.Level == value);
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

    private void SetNotContainsComparison(dynamic? value)
    {
        switch (Value.FilterType)
        {
            case FilterType.EventId :
                Value.Comparison.Add(x => !x.Id.ToString().Contains(value?.ToString()));
                break;
            case FilterType.Level :
                Value.Comparison.Add(x => !x.Level.ToString()?.Contains(value?.ToString()));
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

    private void SetNotEqualsComparison(dynamic? value)
    {
        switch (Value.FilterType)
        {
            case FilterType.EventId :
                Value.Comparison.Add(x => x.Id != value);
                break;
            case FilterType.Level :
                Value.Comparison.Add(x => x.Level != value);
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
}
