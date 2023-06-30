// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
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
    [Parameter] public FilterModel Value { get; set; } = null!;

    [Inject] private IAlertDialogService AlertDialogService { get; set; } = null!;

    [Inject] private IDispatcher Dispatcher { get; set; } = null!;

    private List<string> FilteredItems =>
        Items.Where(x => x.ToLower().Contains(Value.FilterValue?.ToLower() ?? string.Empty)).ToList();

    private List<string> Items
    {
        get
        {
            switch (Value.FilterType)
            {
                case FilterType.Id :
                    return EventLogState.Value.ActiveLogs.Values.SelectMany(log => log.EventIds)
                        .Distinct().OrderBy(id => id).Select(id => id.ToString()).ToList();
                case FilterType.Level :
                    var items = new List<string>();

                    foreach (SeverityLevel item in Enum.GetValues(typeof(SeverityLevel)))
                    {
                        items.Add(item.ToString());
                    }

                    return items;
                case FilterType.KeywordsDisplayNames :
                    return EventLogState.Value.ActiveLogs.Values.SelectMany(log => log.KeywordNames)
                        .Distinct().OrderBy(name => name).Select(name => name.ToString()).ToList();
                case FilterType.Source :
                    return EventLogState.Value.ActiveLogs.Values.SelectMany(log => log.EventProviderNames)
                        .Distinct().OrderBy(name => name).Select(name => name.ToString()).ToList();
                case FilterType.TaskCategory :
                    return EventLogState.Value.ActiveLogs.Values.SelectMany(log => log.TaskNames)
                        .Distinct().OrderBy(name => name).Select(name => name.ToString()).ToList();
                case FilterType.Description :
                default :
                    return new List<string>();
            }
        }
    }

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
            FilterMethods.GetComparison(
                newModel.FilterComparison,
                newModel.FilterType,
                newModel.FilterValue,
                newModel.FilterValues);

        if (comparison is null) { return; }

        comparisons.Add(comparison);

        if (Value.SubFilters.Count > 0)
        {
            foreach (var subFilter in Value.SubFilters)
            {
                comparison = FilterMethods.GetComparison(
                    subFilter.FilterComparison,
                    subFilter.FilterType,
                    subFilter.FilterValue,
                    subFilter.FilterValues);

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
