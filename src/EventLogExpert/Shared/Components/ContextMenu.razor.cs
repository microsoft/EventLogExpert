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
using System.Collections.Immutable;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Shared.Components;

public sealed partial class ContextMenu
{
    [Inject] private IClipboardService ClipboardService { get; init; } = null!;

    [Inject] private IDispatcher Dispatcher { get; init; } = null!;

    [Inject]
    private IStateSelection<EventLogState, ImmutableList<DisplayEventModel>> SelectedEventState { get; init; } = null!;

    protected override void OnInitialized()
    {
        SelectedEventState.Select(s => s.SelectedEvents);

        base.OnInitialized();
    }

    private void CopySelected(CopyType? copyType) => ClipboardService.CopySelectedEvent(copyType);

    private void FilterEvent(FilterCategory filterType, bool shouldExclude = false)
    {
        if (SelectedEventState.Value.IsEmpty) { return; }

        var selectedEvent = SelectedEventState.Value.Last();

        string filterValue = filterType switch
        {
            FilterCategory.Id => selectedEvent.Id.ToString(),
            FilterCategory.ActivityId => selectedEvent.ActivityId.ToString()!,
            FilterCategory.Level => selectedEvent.Level,
            FilterCategory.KeywordsDisplayNames => selectedEvent.KeywordsDisplayNames.GetEventKeywords(),
            FilterCategory.Source => selectedEvent.Source,
            FilterCategory.TaskCategory => selectedEvent.TaskCategory,
            FilterCategory.Description => selectedEvent.Description,
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
