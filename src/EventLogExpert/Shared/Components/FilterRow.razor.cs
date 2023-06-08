// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using EventLogExpert.Store.FilterPane;
using EventLogExpert.UI;
using EventLogExpert.UI.Models;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Components;

public partial class FilterRow
{
    [Parameter] public FilterModel Value { get; set; } = null!;

    private void AddSubFilter() => Dispatcher.Dispatch(new FilterPaneAction.AddSubFilter(Value.Id));

    private void EditFilter() => Dispatcher.Dispatch(new FilterPaneAction.ToggleEditFilter(Value.Id));

    private void RemoveFilter() => Dispatcher.Dispatch(new FilterPaneAction.RemoveFilter(Value.Id));

    private async void SaveFilter()
    {
        FilterModel newModel = Value with { };

        if (!FilterMethods.TryParse(newModel, out string? comparisonString))
        {
            if (Application.Current?.MainPage is not null)
            {
                await Application.Current.MainPage.DisplayAlert("Invalid Filter",
                    "The filter you have created is an invalid filter, please adjust and try again.",
                    "Ok");
            }

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
                    if (Application.Current?.MainPage is not null)
                    {
                        await Application.Current.MainPage.DisplayAlert("Invalid Filter",
                            "The sub filter you have created is an invalid filter, please adjust and try again.",
                            "Ok");
                    }

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
