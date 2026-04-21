// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Store.EventLog;
using EventLogExpert.UI.Store.FilterPane;
using Fluxor;
using Microsoft.AspNetCore.Components;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Shared.Components.Filters;

public sealed partial class FilterRow
{
    [Parameter] public string Id { get; set; } = Guid.NewGuid().ToString();

    [Parameter] public FilterModel Value { get; set; } = null!;

    [Inject] private IAlertDialogService AlertDialogService { get; init; } = null!;

    /// <summary>Wraps <c>Value.Data.Category</c> so that a transition into a text-only category
    /// (Description / Xml) auto-corrects an incompatible evaluator. Without this, switching from
    /// e.g. Source (MultiSelect) to Xml would leave Evaluator=MultiSelect with no matching option
    /// in the comparison dropdown — producing an invalid filter on save.</summary>
    private FilterCategory CategoryBinding
    {
        get => Value.Data.Category;
        set
        {
            Value.Data.Category = value;

            if (IsTextOnlyCategory(value) && Value.Data.Evaluator == FilterEvaluator.MultiSelect)
            {
                Value.Data.Evaluator = FilterEvaluator.Contains;
            }
        }
    }

    [Inject] private IDispatcher Dispatcher { get; init; } = null!;

    [Inject] private IState<EventLogState> EventLogState { get; init; } = null!;

    private List<string> FilteredItems => Items
        .Where(x => x.Contains(Value.Data.Value ?? string.Empty, StringComparison.CurrentCultureIgnoreCase))
        .ToList();

    [Inject] private IFilterService FilterService { get; init; } = null!;

    private List<string> Items =>
        Value.Data.Category switch
        {
            FilterCategory.Id => [.. EventLogState.Value.ActiveLogs.Values
                .SelectMany(log => log.GetCategoryValues(FilterCategory.Id))
                .Distinct().Order()],
            FilterCategory.ActivityId => [.. EventLogState.Value.ActiveLogs.Values
                .SelectMany(log => log.GetCategoryValues(FilterCategory.ActivityId))
                .Distinct().Order()],
            FilterCategory.Level => [.. Enum.GetNames<SeverityLevel>()],
            FilterCategory.Keywords => [.. EventLogState.Value.ActiveLogs.Values
                .SelectMany(log => log.GetCategoryValues(FilterCategory.Keywords))
                .Distinct().Order()],
            FilterCategory.Source => [.. EventLogState.Value.ActiveLogs.Values
                .SelectMany(log => log.GetCategoryValues(FilterCategory.Source))
                .Distinct().Order()],
            FilterCategory.TaskCategory => [.. EventLogState.Value.ActiveLogs.Values
                .SelectMany(log => log.GetCategoryValues(FilterCategory.TaskCategory))
                .Distinct().Order()],
            _ => []
        };

    /// <summary>Categories whose value is free-form text (no fixed enumerable set of options).
    /// These render as a single text input and disallow MultiSelect comparison.</summary>
    private static bool IsTextOnlyCategory(FilterCategory category) =>
        category is FilterCategory.Description or FilterCategory.Xml;

    private void AddSubFilter() => Dispatcher.Dispatch(new FilterPaneAction.AddSubFilter(Value.Id));

    private void EditFilter() => Dispatcher.Dispatch(new FilterPaneAction.ToggleFilterEditing(Value.Id));

    private void RemoveFilter() => Dispatcher.Dispatch(new FilterPaneAction.RemoveFilter(Value.Id));

    private async Task SaveFilter()
    {
        if (!FilterService.TryParse(Value, out string comparisonString))
        {
            await AlertDialogService.ShowAlert("Invalid Filter",
                "The filter you have created is an invalid filter, please adjust and try again.",
                "Ok");

            return;
        }

        FilterModel newModel = Value with
        {
            Comparison = new FilterComparison { Value = comparisonString },
            IsEditing = false,
            IsEnabled = true
        };

        Dispatcher.Dispatch(new FilterPaneAction.SetFilter(newModel));
    }

    private void ToggleFilter() => Dispatcher.Dispatch(new FilterPaneAction.ToggleFilterEnabled(Value.Id));

    private void ToggleFilterExclusion() =>
        Dispatcher.Dispatch(new FilterPaneAction.ToggleFilterExcluded(Value.Id));
}
