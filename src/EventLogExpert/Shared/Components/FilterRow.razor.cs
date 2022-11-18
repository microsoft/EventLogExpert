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

    //[Parameter] public EventCallback<object?> ValueChanged { get; set; }

    private void EditFilter()
    {
        Value.IsEditing = true;
    }

    //private Task OnValueChanged(ChangeEventArgs e)
    //{
    //    Value.FilterValue = e.Value;
    //    return ValueChanged.InvokeAsync(Value.FilterValue);
    //}

    private void RemoveFilter()
    {
        Dispatcher.Dispatch(new FilterPaneAction.RemoveFilter(Value));
    }

    private void SaveFilter()
    {
        if (SetComparisonString())
        {
            // Set the actual filter
            SetComparison();
        }

        Value.IsEditing = false;

        Dispatcher.Dispatch(new FilterPaneAction.ApplyFilters());
    }

    private void SetComparison()
    {
        switch (Value.FilterComparison)
        {
            case FilterComparison.Equals :
                SetEqualsComparison();
                break;
            case FilterComparison.Contains :
                SetContainsComparison();
                break;
            case FilterComparison.NotEqual :
                SetNotEqualsComparison();
                break;
            default :
                return;
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
        Value.ComparisonString = stringBuilder.ToString();

        return true;
    }

    private void SetContainsComparison()
    {
        switch (Value.FilterType)
        {
            case FilterType.EventId :
                // TODO: Clean this up, extension method?
                Value.Comparison = x => x.Id.ToString().Contains(Value.FilterIntValue.ToString()!);
                break;
            case FilterType.Severity :
                Value.Comparison = x => x.Level.ToString()!.Contains(Value.FilterSeverityValue.ToString()!);
                break;
            case FilterType.Provider :
                Value.Comparison = x =>
                    x.ProviderName.Contains(Value.FilterStringValue!, StringComparison.InvariantCultureIgnoreCase);

                break;
            case FilterType.Task :
                Value.Comparison = x =>
                    x.TaskDisplayName.Contains(Value.FilterStringValue!, StringComparison.InvariantCultureIgnoreCase);

                break;
            case FilterType.Description :
                Value.Comparison = x =>
                    x.Description.Contains(Value.FilterStringValue!, StringComparison.InvariantCultureIgnoreCase);

                break;
            default :
                return;
        }
    }

    private void SetEqualsComparison()
    {
        switch (Value.FilterType)
        {
            case FilterType.EventId :
                Value.Comparison = x => x.Id == Value.FilterIntValue;
                break;
            case FilterType.Severity :
                Value.Comparison = x => x.Level == Value.FilterSeverityValue;
                break;
            case FilterType.Provider :
                Value.Comparison = x => string.Equals(x.ProviderName,
                    Value.FilterStringValue,
                    StringComparison.InvariantCultureIgnoreCase);

                break;
            case FilterType.Task :
                Value.Comparison = x => string.Equals(x.TaskDisplayName,
                    Value.FilterStringValue,
                    StringComparison.InvariantCultureIgnoreCase);

                break;
            case FilterType.Description :
                Value.Comparison = x => string.Equals(x.Description,
                    Value.FilterStringValue,
                    StringComparison.InvariantCultureIgnoreCase);

                break;
            default :
                return;
        }
    }

    private void SetNotEqualsComparison()
    {
        switch (Value.FilterType)
        {
            case FilterType.EventId :
                Value.Comparison = x => x.Id != Value.FilterIntValue;
                break;
            case FilterType.Severity :
                Value.Comparison = x => x.Level != Value.FilterSeverityValue;
                break;
            case FilterType.Provider :
                Value.Comparison = x => !string.Equals(x.ProviderName,
                    Value.FilterStringValue,
                    StringComparison.InvariantCultureIgnoreCase);

                break;
            case FilterType.Task :
                Value.Comparison = x => !string.Equals(x.TaskDisplayName,
                    Value.FilterStringValue,
                    StringComparison.InvariantCultureIgnoreCase);

                break;
            case FilterType.Description :
                Value.Comparison = x => !string.Equals(x.Description,
                    Value.FilterStringValue,
                    StringComparison.InvariantCultureIgnoreCase);

                break;
            default :
                return;
        }
    }
}
