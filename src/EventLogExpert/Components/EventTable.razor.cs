// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.UI;
using EventLogExpert.UI.Store.EventLog;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Collections.Immutable;
using static EventLogExpert.UI.Store.EventLog.EventLogState;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Components;

public partial class EventTable
{
    private string? _activeLog;

    [Inject]
    private IStateSelection<EventLogState, IImmutableDictionary<string, EventLogData>> ActiveLogState { get; set; } = null!;

    [Inject] private IDispatcher Dispatcher { get; set; } = null!;

    [Inject] private IJSRuntime JSRuntime { get; set; } = null!;

    [Inject]
    private IStateSelection<EventLogState, DisplayEventModel?> SelectedEventState { get; set; } = null!;

    [Inject] private ITraceLogger TraceLogger { get; set; } = null!;

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

    private static string GetLevelClass(SeverityLevel? level) => level switch
    {
        SeverityLevel.Error => "bi bi-exclamation-circle error",
        SeverityLevel.Warning => "bi bi-exclamation-triangle warning",
        SeverityLevel.Information => "bi bi-info-circle",
        _ => "",
    };

    private string GetCss(DisplayEventModel @event) => SelectedEventState.Value?.RecordId == @event.RecordId ?
        "table-row selected" : "table-row";

    private async Task InvokeContextMenu(MouseEventArgs args) =>
        await JSRuntime.InvokeVoidAsync("invokeContextMenu", args.ClientX, args.ClientY);

    private async Task InvokeTableColumnMenu(MouseEventArgs args) =>
        await JSRuntime.InvokeVoidAsync("invokeTableColumnMenu", args.ClientX, args.ClientY);

    private bool IsColumnHidden(ColumnName columnName)
    {
        if (!SettingsState.Value.EventTableColumns.TryGetValue(columnName, out var enabled)) { return true; }

        return !enabled;
    }

    private void SelectEvent(DisplayEventModel @event) => Dispatcher.Dispatch(new EventLogAction.SelectEvent(@event));

    private void ToggleDateTime() =>
        Dispatcher.Dispatch(new EventLogAction.SetSortDescending(!EventLogState.Value.SortDescending, TraceLogger));
}
