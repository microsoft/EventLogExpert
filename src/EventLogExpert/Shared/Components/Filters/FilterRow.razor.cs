// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using EventLogExpert.UI;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Store.FilterPane;
using Microsoft.AspNetCore.Components;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Shared.Components.Filters;

public partial class FilterRow
{
    private List<string> _filterItems = new();

    [Parameter] public FilterModel Value { get; set; } = null!;

    [Inject] private IAlertDialogService AlertDialogService { get; set; } = null!;

    [Inject] private IDispatcher Dispatcher { get; set; } = null!;

    private List<string> FilteredItems =>
        _filterItems.Where(x => x.ToLower().Contains(Value.FilterValue.ToLower())).ToList();

    private void AddSubFilter() => Dispatcher.Dispatch(new FilterPaneAction.AddSubFilter(Value.Id));

    private void EditFilter() => Dispatcher.Dispatch(new FilterPaneAction.ToggleEditFilter(Value.Id));

    private void RemoveFilter() => Dispatcher.Dispatch(new FilterPaneAction.RemoveFilter(Value.Id));

    private async void SaveFilter()
    {
        FilterModel newModel = Value with { };

        if (!FilterMethods.TryParse(newModel, out string? comparisonString))
        {
            await AlertDialogService.ShowAlert("Invalid Filter",
                "The filter you have created is an invalid filter, please adjust and try again.",
                "Ok");

            return;
        }

        List<Func<DisplayEventModel, bool>> comparisons = new();

        var comparison =
            FilterMethods.GetComparison(newModel.FilterComparison, newModel.FilterType, newModel.FilterValue);

        if (comparison is null) { return; }

        comparisons.Add(comparison);

        if (Value.SubFilters.Count > 0)
        {
            foreach (var subFilter in Value.SubFilters)
            {
                comparison = FilterMethods.GetComparison(subFilter.FilterComparison,
                    subFilter.FilterType,
                    subFilter.FilterValue);

                if (comparison is null)
                {
                    await AlertDialogService.ShowAlert("Invalid Filter",
                        "The sub filter you have created is an invalid filter, please adjust and try again.",
                        "Ok");

                    return;
                }

                comparisons.Add(comparison);
            }
        }

        newModel.ComparisonString = comparisonString;
        newModel.Comparison = comparisons;

        Dispatcher.Dispatch(new FilterPaneAction.SetFilter(newModel));
        Dispatcher.Dispatch(new FilterPaneAction.ToggleEditFilter(newModel.Id));
    }

    private void ToggleFilter() => Dispatcher.Dispatch(new FilterPaneAction.ToggleEnableFilter(Value.Id));
}
