// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Store.EventLog;
using EventLogExpert.UI.Store.EventTable;
using EventLogExpert.UI.Store.FilterPane;
using EventLogExpert.UI.Store.StatusBar;
using Fluxor;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Components;

public sealed partial class StatusBar
{
    private EventLogState _eventLogState = null!;
    private EventTableState _eventTableState = null!;
    private FilterPaneState _filterPaneState = null!;
    private StatusBarState _statusBarState = null!;

    [Inject] private IState<EventLogState> EventLogState { get; set; } = null!;

    [Inject] private IState<EventTableState> EventTableState { get; set; } = null!;

    [Inject] private IState<FilterPaneState> FilterPaneState { get; set; } = null!;

    [Inject] private IState<StatusBarState> StatusBarState { get; set; } = null!;

    protected override void OnInitialized()
    {
        base.OnInitialized();

        _eventLogState = EventLogState.Value;
        _eventTableState = EventTableState.Value;
        _filterPaneState = FilterPaneState.Value;
        _statusBarState = StatusBarState.Value;
    }

    protected override bool ShouldRender()
    {
        if (ReferenceEquals(EventLogState.Value, _eventLogState) &&
            ReferenceEquals(EventTableState.Value, _eventTableState) &&
            ReferenceEquals(FilterPaneState.Value, _filterPaneState) &&
            ReferenceEquals(StatusBarState.Value, _statusBarState)) { return false; }

        _eventLogState = EventLogState.Value;
        _eventTableState = EventTableState.Value;
        _filterPaneState = FilterPaneState.Value;
        _statusBarState = StatusBarState.Value;

        return true;
    }
}
