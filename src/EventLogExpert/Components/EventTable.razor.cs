// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.Services;
using EventLogExpert.UI;
using EventLogExpert.UI.Store.EventLog;
using EventLogExpert.UI.Store.EventTable;
using EventLogExpert.UI.Store.FilterPane;
using EventLogExpert.UI.Store.Settings;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Components;

public sealed partial class EventTable
{
    private EventTableState _eventTableState = null!;
    private DisplayEventModel? _selectedEventState;
    private TimeZoneInfo _timeZoneSettings = null!;

    [Inject] private IClipboardService ClipboardService { get; init; } = null!;

    [Inject] private IDispatcher Dispatcher { get; init; } = null!;

    [Inject] private IState<EventTableState> EventTableState { get; init; } = null!;

    [Inject] private IState<FilterPaneState> FilterPaneState { get; init; } = null!;

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    [Inject] private IStateSelection<EventLogState, DisplayEventModel?> SelectedEventState { get; init; } = null!;

    [Inject] private IStateSelection<SettingsState, TimeZoneInfo> TimeZoneSettings { get; init; } = null!;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await JSRuntime.InvokeVoidAsync("registerTableEvents");
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override async Task OnInitializedAsync()
    {
        SelectedEventState.Select(s => s.SelectedEvent);
        TimeZoneSettings.Select(settings => settings.Config.TimeZoneInfo);

        SubscribeToAction<EventTableAction.SetActiveTable>(action => ScrollToSelectedEvent().AndForget());
        SubscribeToAction<EventTableAction.UpdateCombinedEvents>(action => ScrollToSelectedEvent().AndForget());
        SubscribeToAction<EventTableAction.UpdateDisplayedEvents>(action => ScrollToSelectedEvent().AndForget());

        await base.OnInitializedAsync();
    }

    protected override bool ShouldRender()
    {
        if (ReferenceEquals(EventTableState.Value, _eventTableState) &&
            ReferenceEquals(SelectedEventState.Value, _selectedEventState) &&
            TimeZoneSettings.Value.Equals(_timeZoneSettings)) { return false; }

        _eventTableState = EventTableState.Value;
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
        SelectedEventState.Value?.RecordId == @event.RecordId ?
            "table-row selected" : $"table-row {GetHighlightedColor(@event)}";

    private string GetDateColumnHeader() =>
        TimeZoneSettings.Value.Equals(TimeZoneInfo.Local) ?
            "Date and Time" :
            $"Date and Time {TimeZoneSettings.Value.DisplayName.Split(" ").First()}";

    private string GetHighlightedColor(DisplayEventModel @event)
    {
        foreach (var filter in FilterPaneState.Value.AdvancedFilters.Where(filter =>
            filter.IsEnabled && filter.Comparison.Expression(@event)))
        {
            return filter.Color.Equals(HighlightColor.None) ? string.Empty : filter.Color.ToString().ToLower();
        }

        foreach (var filter in FilterPaneState.Value.BasicFilters.Where(filter =>
            filter.IsEnabled && filter.Comparison.Expression(@event)))
        {
            return filter.Color.Equals(HighlightColor.None) ? string.Empty : filter.Color.ToString().ToLower();
        }

        foreach (var filter in FilterPaneState.Value.CachedFilters.Where(filter =>
            filter.IsEnabled && filter.Comparison.Expression(@event)))
        {
            return filter.Color.Equals(HighlightColor.None) ? string.Empty : filter.Color.ToString().ToLower();
        }

        return string.Empty;
    }

    private void HandleKeyUp(KeyboardEventArgs args)
    {
        // https://developer.mozilla.org/en-US/docs/Web/API/UI_Events/Keyboard_event_key_values
        switch (args)
        {
            case { CtrlKey: true, Code: "KeyC" }:
                ClipboardService.CopySelectedEvent();
                break;
        }
    }

    private async Task InvokeContextMenu(MouseEventArgs args) =>
        await JSRuntime.InvokeVoidAsync("invokeContextMenu", args.ClientX, args.ClientY);

    private async Task InvokeTableColumnMenu(MouseEventArgs args) =>
        await JSRuntime.InvokeVoidAsync("invokeTableColumnMenu", args.ClientX, args.ClientY);

    private bool IsColumnHidden(ColumnName columnName)
    {
        if (!EventTableState.Value.Columns.TryGetValue(columnName, out var enabled)) { return true; }

        return !enabled;
    }

    private async Task ScrollToSelectedEvent()
    {
        var table = EventTableState.Value.EventTables.FirstOrDefault(x => x.Id == EventTableState.Value.ActiveEventLogId);

        var entry = table?.DisplayedEvents.FirstOrDefault(x =>
            string.Equals(x.LogName, SelectedEventState.Value?.LogName) &&
            x.RecordId == SelectedEventState.Value?.RecordId);

        if (table is null || entry is null) { return; }

        var index = table.DisplayedEvents.IndexOf(entry);

        if (index >= 0)
        {
            await JSRuntime.InvokeVoidAsync("scrollToRow", index);
        }
    }

    private void SelectEvent(DisplayEventModel @event) => Dispatcher.Dispatch(new EventLogAction.SelectEvent(@event));

    private void ToggleSorting() => Dispatcher.Dispatch(new EventTableAction.ToggleSorting());
}
