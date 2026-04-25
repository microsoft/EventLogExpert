// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using EventLogExpert.Services;
using EventLogExpert.UI;
using EventLogExpert.UI.Interfaces;
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

    [Inject] private IFilterService FilterService { get; init; } = null!;

    [Inject]
    private IStateSelection<EventLogState, DisplayEventModel?> SelectedEvent { get; init; } = null!;

    protected override void OnInitialized()
    {
        SelectedEvent.Select(s => s.SelectedEvent);

        base.OnInitialized();
    }

    private async Task CopySelected(CopyType? copyType) => await ClipboardService.CopySelectedEvent(copyType);

    private void ExcludeAfter() =>
        Dispatcher.Dispatch(
            new FilterPaneAction.SetFilterDateRange(
                new FilterDateModel { After = SelectedEvent.Value?.TimeCreated }));

    private void ExcludeBefore() =>
        Dispatcher.Dispatch(
            new FilterPaneAction.SetFilterDateRange(
                new FilterDateModel { Before = SelectedEvent.Value?.TimeCreated }));

    private void FilterEvent(FilterCategory filterType, bool shouldExclude = false)
    {
        var selectedEvent = SelectedEvent.Value;

        if (selectedEvent is null) { return; }

        string filterValue = filterType switch
        {
            FilterCategory.Id => selectedEvent.Id.ToString(),
            FilterCategory.ActivityId => selectedEvent.ActivityId.ToString()!,
            FilterCategory.Level => selectedEvent.Level,
            FilterCategory.Keywords => selectedEvent.KeywordsDisplayName,
            FilterCategory.Source => selectedEvent.Source,
            FilterCategory.TaskCategory => selectedEvent.TaskCategory,
            _ => string.Empty,
        };

        var basicSource = new BasicFilterSource(
            new BasicFilterCriteria
            {
                Category = filterType,
                Evaluator = FilterEvaluator.Equals,
                Value = filterValue
            },
            []);

        if (!FilterService.TryParse(basicSource, out var comparisonString)) { return; }

        var filter = FilterModel.TryCreate(
            comparisonString,
            FilterType.Basic,
            basicSource,
            isExcluded: shouldExclude,
            isEnabled: true);

        if (filter is null) { return; }

        Dispatcher.Dispatch(new FilterPaneAction.SetFilter(filter));
    }
}
