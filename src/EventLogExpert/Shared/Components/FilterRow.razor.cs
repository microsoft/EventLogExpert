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

    private void RemoveFilter()
    {
        Dispatcher.Dispatch(new FilterPaneAction.RemoveFilter(Value));
        Dispatcher.Dispatch(new FilterPaneAction.ApplyFilters());
    }

    private void SaveFilter()
    {
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

    // TODO: This could probably be refactored way better
    private void SetComparison(FilterModel? filterModel = null, SubFilterModel? subFilterModel = null)
    {
        if (filterModel is not null)
        {
            switch (filterModel.FilterComparison)
            {
                case FilterComparison.Equals :
                    SetEqualsComparison(filterModel.FilterIntValue,
                        filterModel.FilterSeverityValue,
                        filterModel.FilterStringValue);

                    break;
                case FilterComparison.Contains :
                    SetContainsComparison(filterModel.FilterIntValue,
                        filterModel.FilterSeverityValue,
                        filterModel.FilterStringValue);

                    break;
                case FilterComparison.NotEqual :
                    SetNotEqualsComparison(filterModel.FilterIntValue,
                        filterModel.FilterSeverityValue,
                        filterModel.FilterStringValue);

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
                    SetEqualsComparison(subFilterModel.FilterIntValue,
                        subFilterModel.FilterSeverityValue,
                        subFilterModel.FilterStringValue);

                    break;
                case FilterComparison.Contains :
                    SetContainsComparison(subFilterModel.FilterIntValue,
                        subFilterModel.FilterSeverityValue,
                        subFilterModel.FilterStringValue);

                    break;
                case FilterComparison.NotEqual :
                    SetNotEqualsComparison(subFilterModel.FilterIntValue,
                        subFilterModel.FilterSeverityValue,
                        subFilterModel.FilterStringValue);

                    break;
                default :
                    return;
            }
        }
    }

    private bool SetComparisonString()
    {
        if (Value.FilterIntValue is null && Value.FilterSeverityValue is null && Value.FilterStringValue is null)
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
            default :
                return false;
        }

        string? filterType;

        switch (Value.FilterType)
        {
            case FilterType.EventId :
                filterType = Value.FilterIntValue.ToString();
                break;
            case FilterType.Severity :
                filterType = Value.FilterSeverityValue.ToString();
                break;
            case FilterType.Provider :
            case FilterType.Task :
            case FilterType.Description :
                filterType = Value.FilterStringValue;
                break;
            default :
                return false;
        }

        if (string.IsNullOrEmpty(filterType))
        {
            Value.ComparisonString = null;
            return false;
        }

        stringBuilder.AppendLine(filterType);

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

    private string? GetSubFilterComparisonString(SubFilterModel subFilter)
    {
        if (subFilter.FilterIntValue is null && subFilter.FilterSeverityValue is null && subFilter.FilterStringValue is null)
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
            default :
                return null;
        }

        string? filterType;

        switch (Value.FilterType)
        {
            case FilterType.EventId :
                filterType = subFilter.FilterIntValue.ToString();
                break;
            case FilterType.Severity :
                filterType = subFilter.FilterSeverityValue.ToString();
                break;
            case FilterType.Provider :
            case FilterType.Task :
            case FilterType.Description :
                filterType = subFilter.FilterStringValue;
                break;
            default :
                return null;
        }

        if (string.IsNullOrEmpty(filterType)) { return null; }

        stringBuilder.AppendLine(filterType);
        return stringBuilder.ToString();
    }

    private void SetContainsComparison(int? filterInt, SeverityLevel? filterSeverity, string? filterString)
    {
        switch (Value.FilterType)
        {
            case FilterType.EventId :
                Value.Comparison.Add(x => x.Id.ToString().Contains(filterInt.ToString()!));
                break;
            case FilterType.Severity :
                Value.Comparison.Add(x => x.Level.ToString()!.Contains(filterSeverity.ToString()!));
                break;
            case FilterType.Provider :
                Value.Comparison.Add(x =>
                    x.ProviderName.Contains(filterString!, StringComparison.InvariantCultureIgnoreCase));

                break;
            case FilterType.Task :
                Value.Comparison.Add(x =>
                    x.TaskDisplayName.Contains(filterString!, StringComparison.InvariantCultureIgnoreCase));

                break;
            case FilterType.Description :
                Value.Comparison.Add(x =>
                    x.Description.Contains(filterString!, StringComparison.InvariantCultureIgnoreCase));

                break;
            default :
                return;
        }
    }

    private void SetEqualsComparison(int? filterInt, SeverityLevel? filterSeverity, string? filterString)
    {
        switch (Value.FilterType)
        {
            case FilterType.EventId :
                Value.Comparison.Add(x => x.Id == filterInt);
                break;
            case FilterType.Severity :
                Value.Comparison.Add(x => x.Level == filterSeverity);
                break;
            case FilterType.Provider :
                Value.Comparison.Add(x =>
                    string.Equals(x.ProviderName, filterString, StringComparison.InvariantCultureIgnoreCase));

                break;
            case FilterType.Task :
                Value.Comparison.Add(x =>
                    string.Equals(x.TaskDisplayName, filterString, StringComparison.InvariantCultureIgnoreCase));

                break;
            case FilterType.Description :
                Value.Comparison.Add(x =>
                    string.Equals(x.Description, filterString, StringComparison.InvariantCultureIgnoreCase));

                break;
            default :
                return;
        }
    }

    private void SetNotEqualsComparison(int? filterInt, SeverityLevel? filterSeverity, string? filterString)
    {
        switch (Value.FilterType)
        {
            case FilterType.EventId :
                Value.Comparison.Add(x => x.Id != filterInt);
                break;
            case FilterType.Severity :
                Value.Comparison.Add(x => x.Level != filterSeverity);
                break;
            case FilterType.Provider :
                Value.Comparison.Add(x =>
                    !string.Equals(x.ProviderName, filterString, StringComparison.InvariantCultureIgnoreCase));

                break;
            case FilterType.Task :
                Value.Comparison.Add(x =>
                    !string.Equals(x.TaskDisplayName, filterString, StringComparison.InvariantCultureIgnoreCase));

                break;
            case FilterType.Description :
                Value.Comparison.Add(x =>
                    !string.Equals(x.Description, filterString, StringComparison.InvariantCultureIgnoreCase));

                break;
            default :
                return;
        }
    }
}
