// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Components.Filters.Base;
using EventLogExpert.UI.Alerts;
using EventLogExpert.UI.Filter;
using EventLogExpert.UI.FilterPane;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Components.Filters;

public sealed partial class FilterRow : EditableFilterRowBase
{
    [Inject] private IAlertDialogService AlertDialogService { get; init; } = null!;

    protected override void DispatchRemoveFilter()
    {
        if (Value is not { } savedFilter) { return; }

        Dispatcher.Dispatch(new RemoveFilterAction(savedFilter.Id));
    }

    protected override void DispatchSetFilter(SavedFilter filter) =>
        Dispatcher.Dispatch(new SetFilterAction(filter));

    protected override void DispatchToggleEnabled(FilterId id) =>
        Dispatcher.Dispatch(new ToggleFilterEnabledAction(id));

    protected override void DispatchToggleExclusion(FilterId id) =>
        Dispatcher.Dispatch(new ToggleFilterExcludedAction(id));

    /// <summary>Structured filter: validate via TryParse and surface failures via the alert dialog (no inline banner).</summary>
    protected override async ValueTask<SavedFilter?> TrySaveAsync(FilterDraft draft)
    {
        var basicFilter = draft.ToBasicFilter();

        if (FilterService.TryParse(basicFilter, out string comparisonString))
        {
            var model = SavedFilter.TryCreate(
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
