// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.Services;
using EventLogExpert.UI;
using EventLogExpert.UI.Store.EventLog;
using EventLogExpert.UI.Store.EventTable;
using EventLogExpert.UI.Store.Settings;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Components;

public sealed partial class EventTable
{
    [Inject] private IClipboardService ClipboardService { get; set; } = null!;

    [Inject] private IDispatcher Dispatcher { get; set; } = null!;

    [Inject] private IState<EventTableState> EventTableState { get; set; } = null!;

    [Inject] private IJSRuntime JSRuntime { get; set; } = null!;

    [Inject] private IStateSelection<EventLogState, DisplayEventModel?> SelectedEventState { get; set; } = null!;

    [Inject] private IState<SettingsState> SettingsState { get; set; } = null!;

    protected override void OnInitialized()
    {
        EventTableState.StateChanged += async (state, args) =>
        {
            if (state is not IState<EventTableState> tableState) { return; }

            foreach (var table in tableState.Value.EventTables)
            {
                if (table.ElementReference is null) { continue; }

                await JSRuntime.InvokeVoidAsync("registerTableEvents", table.ElementReference);
            }
        };

        SelectedEventState.Select(s => s.SelectedEvent);

        base.OnInitialized();
    }

    private static string GetLevelClass(string level) => level switch
    {
        nameof(SeverityLevel.Error) => "bi bi-exclamation-circle error",
        nameof(SeverityLevel.Warning) => "bi bi-exclamation-triangle warning",
        nameof(SeverityLevel.Information) => "bi bi-info-circle",
        _ => string.Empty,
    };

    private string GetCss(DisplayEventModel @event) => SelectedEventState.Value?.RecordId == @event.RecordId ?
        "table-row selected" : "table-row";

    private void HandleKeyUp(KeyboardEventArgs args)
    {
        // https://developer.mozilla.org/en-US/docs/Web/API/UI_Events/Keyboard_event_key_values
        switch (args)
        {
            case { CtrlKey: true, Code: "KeyC" } :
                ClipboardService.CopySelectedEvent(SelectedEventState.Value, SettingsState.Value.Config.CopyType);
                break;
        }
    }

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

    private void ToggleSorting() => Dispatcher.Dispatch(new EventTableAction.ToggleSorting());
}
