// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.EventLog;
using EventLogExpert.UI.FilterPane;
using EventLogExpert.UI.LogTable;
using EventLogExpert.UI.StatusBar;
using Fluxor;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Components.Sections;

public sealed partial class StatusBar
{
    private EventLogState _eventLogState = null!;
    private FilterPaneState _filterPaneState = null!;
    private LogTableState _logTableState = null!;
    private StatusBarState _statusBarState = null!;

    [Inject] private IState<EventLogState> EventLogState { get; set; } = null!;

    [Inject] private IState<FilterPaneState> FilterPaneState { get; set; } = null!;

    [Inject] private IState<LogTableState> LogTableState { get; set; } = null!;

    [Inject] private IState<StatusBarState> StatusBarState { get; set; } = null!;

    protected override void OnInitialized()
    {
        base.OnInitialized();

        _eventLogState = EventLogState.Value;
        _logTableState = LogTableState.Value;
        _filterPaneState = FilterPaneState.Value;
        _statusBarState = StatusBarState.Value;
    }

    protected override bool ShouldRender()
    {
        if (ReferenceEquals(EventLogState.Value, _eventLogState) &&
            ReferenceEquals(LogTableState.Value, _logTableState) &&
            ReferenceEquals(FilterPaneState.Value, _filterPaneState) &&
            ReferenceEquals(StatusBarState.Value, _statusBarState)) { return false; }

        _eventLogState = EventLogState.Value;
        _logTableState = LogTableState.Value;
        _filterPaneState = FilterPaneState.Value;
        _statusBarState = StatusBarState.Value;

        return true;
    }
}
