// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using EventLogExpert.Services;
using EventLogExpert.UI;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.EventLog;
using EventLogExpert.UI.Store.FilterPane;
using Fluxor;
using Microsoft.AspNetCore.Components;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Shared.Components;

public partial class ContextMenu
{
    [Inject] private IClipboardService ClipboardService { get; set; } = null!;

    [Inject] private IDispatcher Dispatcher { get; set; } = null!;

    [Inject]
    private IStateSelection<EventLogState, DisplayEventModel?> SelectedEventState { get; set; } = null!;

    protected override void OnInitialized()
    {
        SelectedEventState.Select(s => s.SelectedEvent);

        base.OnInitialized();
    }

    private void CopySelected(CopyType? copyType) =>
        ClipboardService.CopySelectedEvent(SelectedEventState.Value, copyType);

    private void FilterEvent(FilterType filterType, FilterComparison filterComparison)
    {
        if (SelectedEventState.Value is null) { return; }

        string filterValue = filterType switch
        {
            FilterType.Id => SelectedEventState.Value.Id.ToString(),
            FilterType.ActivityId => SelectedEventState.Value.ActivityId.ToString()!,
            FilterType.Level => SelectedEventState.Value.Level.ToString()!,
            FilterType.KeywordsDisplayNames => SelectedEventState.Value.KeywordsDisplayNames.GetEventKeywords(),
            FilterType.Source => SelectedEventState.Value.Source,
            FilterType.TaskCategory => SelectedEventState.Value.TaskCategory,
            FilterType.Description => SelectedEventState.Value.Description,
            _ => string.Empty,
        };

        FilterModel filter = new()
        {
            IsEditing = false,
            IsEnabled = true,
            FilterType = filterType,
            FilterComparison = filterComparison,
            FilterValue = filterValue
        };

        if (!FilterMethods.TryParse(filter, out string? comparisonString)) { return; }

        filter.ComparisonString = comparisonString!;

        Dispatcher.Dispatch(new FilterPaneAction.SetFilter(filter));
    }
}
