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
    [Parameter] public FilterModel Value { get; set; } = null!;

    [Inject] private IAlertDialogService AlertDialogService { get; init; } = null!;

    [Inject] private IDispatcher Dispatcher { get; init; } = null!;

    [Inject] private IState<EventLogState> EventLogState { get; init; } = null!;

    [Inject] private IFilterService FilterService { get; init; } = null!;

    private List<string> FilteredItems => Items
        .Where(x => x.Contains(Value.Data.Value ?? string.Empty, StringComparison.CurrentCultureIgnoreCase))
        .ToList();

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
            FilterCategory.KeywordsDisplayNames => [.. EventLogState.Value.ActiveLogs.Values
                .SelectMany(log => log.GetCategoryValues(FilterCategory.KeywordsDisplayNames))
                .Distinct().Order()],
            FilterCategory.Source => [.. EventLogState.Value.ActiveLogs.Values
                .SelectMany(log => log.GetCategoryValues(FilterCategory.Source))
                .Distinct().Order()],
            FilterCategory.TaskCategory => [.. EventLogState.Value.ActiveLogs.Values
                .SelectMany(log => log.GetCategoryValues(FilterCategory.TaskCategory))
                .Distinct().Order()],
            _ => []
        };

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
