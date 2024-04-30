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

public sealed partial class ContextMenu
{
    [Inject] private IClipboardService ClipboardService { get; init; } = null!;

    [Inject] private IDispatcher Dispatcher { get; init; } = null!;

    [Inject]
    private IStateSelection<EventLogState, DisplayEventModel?> SelectedEventState { get; init; } = null!;

    protected override void OnInitialized()
    {
        SelectedEventState.Select(s => s.SelectedEvent);

        base.OnInitialized();
    }

    private void CopySelected(CopyType? copyType) => ClipboardService.CopySelectedEvent(copyType);

    private void FilterEvent(FilterCategory filterType, bool shouldExclude = false)
    {
        if (SelectedEventState.Value is null) { return; }

        string filterValue = filterType switch
        {
            FilterCategory.Id => SelectedEventState.Value.Id.ToString(),
            FilterCategory.ActivityId => SelectedEventState.Value.ActivityId.ToString()!,
            FilterCategory.Level => SelectedEventState.Value.Level,
            FilterCategory.KeywordsDisplayNames => SelectedEventState.Value.KeywordsDisplayNames.GetEventKeywords(),
            FilterCategory.Source => SelectedEventState.Value.Source,
            FilterCategory.TaskCategory => SelectedEventState.Value.TaskCategory,
            FilterCategory.Description => SelectedEventState.Value.Description,
            _ => string.Empty,
        };

        FilterModel filter = new()
        {
            Data = new FilterData
            {
                Category = filterType,
                Value = filterValue,
                Evaluator = FilterEvaluator.Equals
            },
            IsEditing = false,
            IsEnabled = true,
            IsExcluded = shouldExclude
        };

        if (!FilterMethods.TryParse(filter, out var comparisonString)) { return; }

        filter.Comparison.Value = comparisonString;

        Dispatcher.Dispatch(new FilterPaneAction.SetFilter(filter));
    }
}
