// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Shared.Base;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Store.FilterPane;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Components.Filters;

public sealed partial class FilterRow : EditableFilterRowBase
{
    [Inject] private IAlertDialogService AlertDialogService { get; init; } = null!;

    [Inject] private IFilterService FilterService { get; init; } = null!;

    protected override void DispatchRemoveFilter() =>
        Dispatcher.Dispatch(new FilterPaneAction.RemoveFilter(Value.Id));

    private void AddSubFilter() => Filter?.SubFilters.Add(new FilterEditorModel());

    private void RemoveSubFilter(FilterId subFilterId) =>
        Filter?.SubFilters.RemoveAll(subFilter => subFilter.Id == subFilterId);

    private async Task SaveFilter()
    {
        if (Filter is null) { return; }

        var draftAsFilter = Filter.ToFilterModel();

        if (!FilterService.TryParse(draftAsFilter, out string comparisonString))
        {
            await AlertDialogService.ShowAlert("Invalid Filter",
                "The filter you have created is an invalid filter, please adjust and try again.",
                "Ok");

            return;
        }

        var newFilter = draftAsFilter with
        {
            Comparison = new FilterComparison { Value = comparisonString },
            IsEditing = false,
            IsEnabled = true
        };

        Filter = null;
        Dispatcher.Dispatch(new FilterPaneAction.SetFilter(newFilter));
        await BubbleSavedAsync();
    }

    private void ToggleFilter() => Dispatcher.Dispatch(new FilterPaneAction.ToggleFilterEnabled(Value.Id));

    private void ToggleFilterExclusion() =>
        Dispatcher.Dispatch(new FilterPaneAction.ToggleFilterExcluded(Value.Id));
}