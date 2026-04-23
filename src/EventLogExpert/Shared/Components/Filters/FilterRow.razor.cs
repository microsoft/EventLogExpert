// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Shared.Base;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Store.FilterPane;
using Microsoft.AspNetCore.Components;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Shared.Components.Filters;

public sealed partial class FilterRow : BaseFilterRow
{
    private FilterEditorModel? _filter;

    [Parameter] public FilterModel Value { get; set; } = null!;

    [Inject] private IAlertDialogService AlertDialogService { get; init; } = null!;

    protected override FilterData CurrentData => _filter?.Data ?? Value.Data;

    [Inject] private IDispatcher Dispatcher { get; init; } = null!;

    [Inject] private IFilterService FilterService { get; init; } = null!;

    protected override void OnParametersSet()
    {
        // Auto-create a draft when the row mounts in edit mode (e.g. AddBasicFilter dispatches
        // AddFilter with IsEditing=true). The `_filter is null` guard ensures we don't overwrite
        // an in-flight draft when the parent re-renders due to unrelated state changes.
        if (Value.IsEditing && _filter is null)
        {
            _filter = FilterEditorModel.FromFilterModel(Value);
        }

        base.OnParametersSet();
    }

    private void AddSubFilter() => _filter?.SubFilters.Add(new FilterEditorModel());

    private void CancelFilter()
    {
        _filter = null;

        // A new filter has no saved comparison string — Cancel removes it entirely. An existing
        // filter just exits edit mode; the saved Value is untouched because the draft was a copy.
        if (string.IsNullOrEmpty(Value.Comparison.Value))
        {
            Dispatcher.Dispatch(new FilterPaneAction.RemoveFilter(Value.Id));
        }
        else
        {
            Dispatcher.Dispatch(new FilterPaneAction.ToggleFilterEditing(Value.Id));
        }
    }

    private void EditFilter()
    {
        _filter = FilterEditorModel.FromFilterModel(Value);
        Dispatcher.Dispatch(new FilterPaneAction.ToggleFilterEditing(Value.Id));
    }

    private void RemoveFilter() => Dispatcher.Dispatch(new FilterPaneAction.RemoveFilter(Value.Id));

    private void RemoveSubFilter(FilterId subFilterId) =>
        _filter?.SubFilters.RemoveAll(subFilter => subFilter.Id == subFilterId);

    private async Task SaveFilter()
    {
        if (_filter is null) { return; }

        var draftAsFilter = _filter.ToFilterModel();

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

        _filter = null;
        Dispatcher.Dispatch(new FilterPaneAction.SetFilter(newFilter));
    }

    private void ToggleFilter() => Dispatcher.Dispatch(new FilterPaneAction.ToggleFilterEnabled(Value.Id));

    private void ToggleFilterExclusion() =>
        Dispatcher.Dispatch(new FilterPaneAction.ToggleFilterExcluded(Value.Id));
}
