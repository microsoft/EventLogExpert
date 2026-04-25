// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Shared.Base;
using EventLogExpert.UI;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Store.FilterPane;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Components.Filters;

public sealed partial class FilterRow : EditableFilterRowBase
{
    [Inject] private IAlertDialogService AlertDialogService { get; init; } = null!;

    protected override void DispatchRemoveFilter()
    {
        if (Value is not { } savedFilter) { return; }

        Dispatcher.Dispatch(new FilterPaneAction.RemoveFilter(savedFilter.Id));
    }

    protected override void DispatchSetFilter(FilterModel filter) =>
        Dispatcher.Dispatch(new FilterPaneAction.SetFilter(filter));

    protected override void DispatchToggleEnabled(FilterId id) =>
        Dispatcher.Dispatch(new FilterPaneAction.ToggleFilterEnabled(id));

    protected override void DispatchToggleExclusion(FilterId id) =>
        Dispatcher.Dispatch(new FilterPaneAction.ToggleFilterExcluded(id));

    /// <summary>Structured filter: validate via TryParse and surface failures via the alert dialog (no inline banner).</summary>
    protected override async ValueTask<FilterModel?> TrySaveAsync(FilterDraftModel draft)
    {
        var basicFilter = draft.ToBasicFilter();

        if (FilterService.TryParse(basicFilter, out string comparisonString))
        {
            var model = FilterModel.TryCreate(
                comparisonString,
                FilterType.Basic,
                basicFilter,
                draft.Color,
                draft.IsExcluded,
                true,
                draft.Id);

            if (model is not null) { return model; }
        }

        await AlertDialogService.ShowAlert("Invalid Filter",
            "The filter you have created is an invalid filter, please adjust and try again.",
            "Ok");

        return null;
    }

    private void AddSubFilter() => Filter?.SubFilters.Add(new SubFilterDraft());

    private void RemoveSubFilter(FilterId subFilterId) =>
        Filter?.SubFilters.RemoveAll(subFilter => subFilter.Id == subFilterId);
}
