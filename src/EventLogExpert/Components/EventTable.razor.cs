// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.Services;
using EventLogExpert.UI;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.EventLog;
using EventLogExpert.UI.Store.EventTable;
using EventLogExpert.UI.Store.FilterPane;
using EventLogExpert.UI.Store.Settings;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Collections.Immutable;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Components;

public sealed partial class EventTable
{
    private EventTableModel? _currentTable;
    private ColumnName[] _enabledColumns = null!;
    private EventTableState _eventTableState = null!;
    private int _rowIndex = 0;
    private ImmutableList<DisplayEventModel> _selectedEventState = [];
    private TimeZoneInfo _timeZoneSettings = null!;

    [Inject] private IClipboardService ClipboardService { get; init; } = null!;

    [Inject] private IDispatcher Dispatcher { get; init; } = null!;

    [Inject] private IState<EventTableState> EventTableState { get; init; } = null!;

    [Inject] private IState<FilterPaneState> FilterPaneState { get; init; } = null!;

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    [Inject] private IStateSelection<EventLogState, ImmutableList<DisplayEventModel>> SelectedEventState { get; init; } = null!;

    [Inject] private IStateSelection<SettingsState, TimeZoneInfo> TimeZoneSettings { get; init; } = null!;

    protected override async Task OnInitializedAsync()
    {
        SelectedEventState.Select(s => s.SelectedEvents);
        TimeZoneSettings.Select(settings => settings.Config.TimeZoneInfo);

        SubscribeToAction<EventTableAction.SetActiveTable>(action => ScrollToSelectedEvent().AndForget());
        SubscribeToAction<EventTableAction.LoadColumnsCompleted>(action => RegisterTableEventHandlers().AndForget());
        SubscribeToAction<EventTableAction.UpdateCombinedEvents>(action => ScrollToSelectedEvent().AndForget());
        SubscribeToAction<EventTableAction.UpdateDisplayedEvents>(action => ScrollToSelectedEvent().AndForget());

        _eventTableState = EventTableState.Value;

        _currentTable = _eventTableState.EventTables.FirstOrDefault(x => x.Id == _eventTableState.ActiveEventLogId);
        _enabledColumns = _eventTableState.Columns.Where(column => column.Value).Select(column => column.Key).ToArray();
        _selectedEventState = SelectedEventState.Value;
        _timeZoneSettings = TimeZoneSettings.Value;

        await base.OnInitializedAsync();
    }

    protected override bool ShouldRender()
    {
        if (ReferenceEquals(EventTableState.Value, _eventTableState) &&
            ReferenceEquals(SelectedEventState.Value, _selectedEventState) &&
            TimeZoneSettings.Value.Equals(_timeZoneSettings)) { return false; }

        _eventTableState = EventTableState.Value;

        _currentTable = _eventTableState.EventTables.FirstOrDefault(x => x.Id == _eventTableState.ActiveEventLogId);
        _enabledColumns = _eventTableState.Columns.Where(column => column.Value).Select(column => column.Key).ToArray();
        _selectedEventState = SelectedEventState.Value;
        _timeZoneSettings = TimeZoneSettings.Value;

        return true;
    }

    private static string GetLevelClass(string level) =>
        level switch
        {
            nameof(SeverityLevel.Error) => "bi bi-exclamation-circle error",
            nameof(SeverityLevel.Warning) => "bi bi-exclamation-triangle warning",
            nameof(SeverityLevel.Information) => "bi bi-info-circle",
            _ => string.Empty,
        };

    private string GetCss(DisplayEventModel @event) =>
        _selectedEventState.Contains(@event) ? "table-row selected" : $"table-row {GetHighlightedColor(@event)}";

    private string GetDateColumnHeader() =>
        TimeZoneSettings.Value.Equals(TimeZoneInfo.Local) ?
            "Date and Time" :
            $"Date and Time {TimeZoneSettings.Value.DisplayName.Split(" ").First()}";

    private string GetHighlightedColor(DisplayEventModel @event)
    {
        foreach (var filter in FilterPaneState.Value.Filters.Where(filter =>
            filter is { IsEnabled: true, IsExcluded: false } && filter.Comparison.Expression(@event)))
        {
            return filter.Color.Equals(HighlightColor.None) ? string.Empty : filter.Color.ToString().ToLower();
        }

        return string.Empty;
    }

    private void HandleKeyDown(KeyboardEventArgs args)
    {
        // https://developer.mozilla.org/en-US/docs/Web/API/UI_Events/Keyboard_event_key_values
        switch (args)
        {
            case { CtrlKey: true, Code: "KeyC" }:
                ClipboardService.CopySelectedEvent();
                return;
        }
    }

    private async Task InvokeContextMenu(MouseEventArgs args) =>
        await JSRuntime.InvokeVoidAsync("invokeContextMenu", args.ClientX, args.ClientY);

    private async Task InvokeTableColumnMenu(MouseEventArgs args) =>
        await JSRuntime.InvokeVoidAsync("invokeTableColumnMenu", args.ClientX, args.ClientY);

    private async Task RegisterTableEventHandlers() => await JSRuntime.InvokeVoidAsync("registerTableEvents");

    private async Task ScrollToSelectedEvent()
    {
        var entry = _currentTable?.DisplayedEvents.FirstOrDefault(x =>
            string.Equals(x.LogName, _selectedEventState.LastOrDefault()?.LogName) &&
            x.RecordId == _selectedEventState.LastOrDefault()?.RecordId);

        if (entry is null) { return; }

        var index = _currentTable?.DisplayedEvents.IndexOf(entry);

        if (index >= 0)
        {
            await JSRuntime.InvokeVoidAsync("scrollToRow", index);
        }
    }

    private void SelectEvent(MouseEventArgs args, DisplayEventModel @event)
    {
        switch (args)
        {
            case { CtrlKey: true }:
                Dispatcher.Dispatch(new EventLogAction.SelectEvent(@event, true));
                return;
            case { ShiftKey: true }:
                var startEvent = _selectedEventState.LastOrDefault();

                if (startEvent is null || _currentTable is null) { return; }

                var startIndex = _currentTable.DisplayedEvents.IndexOf(startEvent);
                var endIndex = _currentTable.DisplayedEvents.IndexOf(@event);

                if (startIndex < endIndex)
                {
                    Dispatcher.Dispatch(new EventLogAction.SelectEvents(
                        _currentTable.DisplayedEvents
                            .Skip(startIndex)
                            .Take(endIndex - startIndex + 1)));
                }
                else
                {
                    Dispatcher.Dispatch(new EventLogAction.SelectEvents(
                        _currentTable.DisplayedEvents
                            .Skip(endIndex)
                            .Take(startIndex - endIndex + 1)));
                }

                return;
            default:
                Dispatcher.Dispatch(new EventLogAction.SelectEvent(@event));
                return;
        }
    }

    private void ToggleSorting() => Dispatcher.Dispatch(new EventTableAction.ToggleSorting());
}
