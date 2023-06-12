// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.Store.EventLog;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Collections.Immutable;
using static EventLogExpert.Store.EventLog.EventLogState;

namespace EventLogExpert.Components;

public partial class EventTable
{
    private string? _activeLog;

    [Inject] private IStateSelection<EventLogState, IImmutableDictionary<string, EventLogData>>
        ActiveLogState { get; set; } = null!;

    [Inject]
    private IStateSelection<EventLogState, DisplayEventModel?> SelectedEventState { get; set; } = null!;

    [Inject] ITraceLogger TraceLogger { get; set; } = null!;

    [Inject] private IJSRuntime JSRuntime { get; set; } = null!;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await JSRuntime.InvokeVoidAsync("registerTableColumnResizers");
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override void OnInitialized()
    {
        MaximumStateChangedNotificationsPerSecond = 2;

        ActiveLogState.Select(s => s.ActiveLogs);

        ActiveLogState.StateChanged += async (sender, activeLog) =>
        {
            await JSRuntime.InvokeVoidAsync("registerTableColumnResizers");
        };

        SelectedEventState.Select(s => s.SelectedEvent);

        base.OnInitialized();
    }

    private string GetCss(DisplayEventModel @event) => SelectedEventState.Value?.RecordId == @event.RecordId ?
        "table-row selected" : "table-row";

    private string GetLevelClass(SeverityLevel? level)
    {
        switch (level)
        {
            case SeverityLevel.Error:
                return "bi bi-exclamation-circle error";
            case SeverityLevel.Warning:
                return "bi bi-exclamation-triangle warning";
            case SeverityLevel.Information:
                return "bi bi-info-circle";
            default:
                return "";
        }
    }

    private void SelectEvent(DisplayEventModel @event) => Dispatcher.Dispatch(new EventLogAction.SelectEvent(@event));

    private void ToggleDateTime() => Dispatcher.Dispatch(new EventLogAction.SetSortDescending(!EventLogState.Value.SortDescending, TraceLogger));
}
