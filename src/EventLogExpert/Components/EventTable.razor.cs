// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.Services;
using EventLogExpert.UI;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.EventLog;
using EventLogExpert.UI.Store.EventTable;
using EventLogExpert.UI.Store.FilterPane;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Collections.Immutable;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Components;

public sealed partial class EventTable
{
    private static readonly Dictionary<HighlightColor, string?> s_highlightNames =
        Enum.GetValues<HighlightColor>().ToDictionary(
            color => color,
            color => color == HighlightColor.None ? null : color.ToString().ToLowerInvariant());

    // Tracks HighlightColor enum values we've already warned about so the
    // warning is emitted at most once per unknown value across the app's
    // lifetime, instead of once per matched event in the GetHighlight hot
    // path. Synchronized because filter updates can flow through Fluxor
    // effects on background threads.
    private static readonly HashSet<int> s_warnedUnknownColors = [];

    private EventTableModel? _currentTable;
    private DotNetObjectReference<EventTable>? _dotNetRef;
    private ColumnName[] _enabledColumns = null!;
    private EventTableState _eventTableState = null!;
    private ImmutableList<FilterModel> _filters = [];
    private string _headerName = string.Empty;
    private Dictionary<DisplayEventModel, string?> _highlightCache = new(ReferenceEqualityComparer.Instance);
    private IReadOnlyList<DisplayEventModel>? _lastDisplayedEvents;
    private ColumnName[] _previousEnabledColumns = [];
    private Dictionary<DisplayEventModel, int> _rowIndexMap = new(ReferenceEqualityComparer.Instance);
    private ImmutableList<DisplayEventModel> _selectedEventState = [];
    private HashSet<DisplayEventModel> _selectedSet = new(ReferenceEqualityComparer.Instance);
    private TimeZoneInfo _timeZoneSettings = null!;

    [Inject] private IClipboardService ClipboardService { get; init; } = null!;

    [Inject] private IDispatcher Dispatcher { get; init; } = null!;

    [Inject] private IState<EventTableState> EventTableState { get; init; } = null!;

    [Inject] private IState<FilterPaneState> FilterPaneState { get; init; } = null!;

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    [Inject] private IStateSelection<EventLogState, ImmutableList<DisplayEventModel>> SelectedEventState { get; init; } = null!;

    [Inject] private ISettingsService Settings { get; init; } = null!;

    [Inject] private ITraceLogger TraceLogger { get; init; } = null!;

    [JSInvokable]
    public void OnColumnReordered(string columnName, string targetColumn, bool insertAfter)
    {
        if (Enum.TryParse<ColumnName>(columnName, out var column) &&
            Enum.TryParse<ColumnName>(targetColumn, out var target))
        {
            Dispatcher.Dispatch(new EventTableAction.ReorderColumn(column, target, insertAfter));
        }
    }

    [JSInvokable]
    public void OnColumnResized(string columnName, int width)
    {
        if (Enum.TryParse<ColumnName>(columnName, out var column))
        {
            Dispatcher.Dispatch(new EventTableAction.SetColumnWidth(column, width));
        }
    }

    protected override async ValueTask DisposeAsyncCore(bool disposing)
    {
        if (disposing)
        {
            try
            {
                await JSRuntime.InvokeVoidAsync("disposeTableEvents");
            }
            catch (JSDisconnectedException) { }

            _dotNetRef?.Dispose();
        }

        await base.DisposeAsyncCore(disposing);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Reinitialize JS when columns change (add/remove/reorder). This
        // ensures resize dividers and event listeners target the current DOM.
        if (firstRender || !_enabledColumns.SequenceEqual(_previousEnabledColumns))
        {
            _previousEnabledColumns = _enabledColumns.ToArray();

            try
            {
                await InitializeTableEventHandlers();
            }
            catch (Exception e)
            {
                TraceLogger.Error($"Failed to initialize table event handlers: {e}");
            }
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override async Task OnInitializedAsync()
    {
        SelectedEventState.Select(s => s.SelectedEvents);

        SubscribeToAction<EventTableAction.SetActiveTable>(OnSetActiveTable);
        SubscribeToAction<EventTableAction.UpdateCombinedEvents>(OnUpdateCombinedEvents);
        SubscribeToAction<EventTableAction.UpdateDisplayedEvents>(OnUpdateDisplayedEvents);

        _eventTableState = EventTableState.Value;

        _currentTable = _eventTableState.EventTables.FirstOrDefault(x => x.Id == _eventTableState.ActiveEventLogId);
        _enabledColumns = GetOrderedEnabledColumns();
        _selectedEventState = SelectedEventState.Value;
        _selectedSet = new HashSet<DisplayEventModel>(_selectedEventState, ReferenceEqualityComparer.Instance);
        _filters = FilterPaneState.Value.Filters;
        _timeZoneSettings = Settings.TimeZoneInfo;

        WarnOnUnknownFilterColors(_filters);
        RebuildRowIndexMap();

        await base.OnInitializedAsync();
    }

    protected override bool ShouldRender()
    {
        // Snapshot once so the short-circuit check and the field assignment
        // below see the same reference even if Fluxor publishes a new state
        // mid-method.
        var currentFilters = FilterPaneState.Value.Filters;
        bool filtersChanged = !ReferenceEquals(currentFilters, _filters);

        if (ReferenceEquals(EventTableState.Value, _eventTableState) &&
            ReferenceEquals(SelectedEventState.Value, _selectedEventState) &&
            !filtersChanged &&
            Settings.TimeZoneInfo.Equals(_timeZoneSettings)) { return false; }

        bool selectionChanged = !ReferenceEquals(SelectedEventState.Value, _selectedEventState);

        _eventTableState = EventTableState.Value;

        _currentTable = _eventTableState.EventTables.FirstOrDefault(x => x.Id == _eventTableState.ActiveEventLogId);
        _enabledColumns = GetOrderedEnabledColumns();

        if (selectionChanged)
        {
            _selectedEventState = SelectedEventState.Value;
            // Reference equality is required because DisplayEventModel is a
            // record whose Xml field can be mutated post-selection by
            // ResolveXml(); value-equality hashes would shift and the row
            // would visibly lose its selected styling on Virtualize re-render.
            _selectedSet = new HashSet<DisplayEventModel>(_selectedEventState, ReferenceEqualityComparer.Instance);
        }

        if (filtersChanged)
        {
            _filters = currentFilters;
            _highlightCache.Clear();
            WarnOnUnknownFilterColors(_filters);
        }

        _timeZoneSettings = Settings.TimeZoneInfo;

        RebuildRowIndexMap();

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

    private int GetColumnWidth(ColumnName column) =>
        _eventTableState.ColumnWidths.TryGetValue(column, out int width) ? width : ColumnDefaults.GetWidth(column);

    private string GetCss(DisplayEventModel @event) =>
        _selectedSet.Contains(@event) ? "table-row selected" : "table-row";

    private string GetDateColumnHeader() =>
        Settings.TimeZoneInfo.Equals(TimeZoneInfo.Local) ?
            "Date and Time" :
            $"Date and Time {Settings.TimeZoneInfo.DisplayName.Split(" ").First()}";

    private string? GetHighlight(DisplayEventModel @event)
    {
        // Selected rows show selection styling (.selected wins via !important);
        // skip cache writes so deselecting doesn't require a refill.
        if (_selectedSet.Contains(@event)) { return null; }

        if (_highlightCache.TryGetValue(@event, out var cached)) { return cached; }

        string? color = null;

        // Preserve existing semantics: first matching enabled+included filter
        // wins (even if its Color is None, which suppresses any later match).
        // Editing filters are excluded because their fields can be mutated in
        // place without producing a new FilterPaneState reference.
        foreach (var filter in _filters)
        {
            if (filter is not { IsEnabled: true, IsExcluded: false, IsEditing: false }) { continue; }
            if (!filter.Comparison.Expression(@event)) { continue; }

            // Skip filters whose Color is outside the defined HighlightColor
            // range (e.g., from corrupted persisted state) so they don't
            // suppress legitimate later matches. Unknown values are reported
            // once when the filter set changes (see WarnOnUnknownFilterColors)
            // rather than per-event from this hot path.
            if (!s_highlightNames.TryGetValue(filter.Color, out var name)) { continue; }

            color = name;
            break;
        }

        _highlightCache[@event] = color;

        return color;
    }

    private ColumnName[] GetOrderedEnabledColumns()
    {
        var enabledSet = _eventTableState.Columns
            .Where(column => column.Value)
            .Select(column => column.Key)
            .ToHashSet();

        if (_eventTableState.ColumnOrder.IsEmpty)
        {
            // Use ColumnDefaults.Order for a deterministic fallback rather than
            // HashSet iteration order, which is not guaranteed.
            return ColumnDefaults.Order.Where(enabledSet.Contains).ToArray();
        }

        return _eventTableState.ColumnOrder
            .Where(enabledSet.Contains)
            .ToArray();
    }

    private int GetRowIndex(DisplayEventModel evt) =>
        _rowIndexMap.TryGetValue(evt, out int index) ? index + 2 : 2;

    private void HandleKeyDown(KeyboardEventArgs args)
    {
        int? currentIndex;
        DisplayEventModel? lastEvent;
        DisplayEventModel? nextEvent;

        // https://developer.mozilla.org/en-US/docs/Web/API/UI_Events/Keyboard_event_key_values
        switch (args)
        {
            case { CtrlKey: true, Code: "KeyC" }:
                ClipboardService.CopySelectedEvent();

                return;
            case { Code: "ArrowUp" }:
                lastEvent = _selectedEventState.LastOrDefault();

                if (lastEvent is null) { return; }

                currentIndex = _currentTable?.DisplayedEvents.IndexOf(lastEvent);

                nextEvent = currentIndex > 0 ? _currentTable?.DisplayedEvents.ElementAtOrDefault(currentIndex.Value - 1) : null;

                if (nextEvent is null) { return; }

                Dispatcher.Dispatch(new EventLogAction.SelectEvent(nextEvent));

                return;
            case { Code: "ArrowDown" }:
                lastEvent = _selectedEventState.LastOrDefault();

                if (lastEvent is null) { return; }

                currentIndex = _currentTable?.DisplayedEvents.IndexOf(lastEvent);

                nextEvent = currentIndex < _currentTable?.DisplayedEvents.Count ? _currentTable?.DisplayedEvents.ElementAtOrDefault(currentIndex.Value + 1) : null;

                if (nextEvent is null) { return; }

                Dispatcher.Dispatch(new EventLogAction.SelectEvent(nextEvent));

                return;
        }
    }

    private async Task InitializeTableEventHandlers()
    {
        _dotNetRef?.Dispose();
        _dotNetRef = DotNetObjectReference.Create(this);
        await JSRuntime.InvokeVoidAsync("initializeTableEvents", _dotNetRef);
    }

    private async Task InvokeContextMenu(MouseEventArgs args) =>
        await JSRuntime.InvokeVoidAsync("invokeContextMenu", args.ClientX, args.ClientY);

    private async Task InvokeTableColumnMenu(MouseEventArgs args) =>
        await JSRuntime.InvokeVoidAsync("invokeTableColumnMenu", args.ClientX, args.ClientY);

    private async void OnSetActiveTable(EventTableAction.SetActiveTable action)
    {
        try
        {
            await InvokeAsync(ScrollToSelectedEvent);
        }
        catch (Exception e)
        {
            TraceLogger.Error($"Failed to scroll to selected event: {e}");
        }
    }

    private async void OnUpdateCombinedEvents(EventTableAction.UpdateCombinedEvents action)
    {
        try
        {
            await InvokeAsync(ScrollToSelectedEvent);
        }
        catch (Exception e)
        {
            TraceLogger.Error($"Failed to scroll to selected event: {e}");
        }
    }

    private async void OnUpdateDisplayedEvents(EventTableAction.UpdateDisplayedEvents action)
    {
        try
        {
            await InvokeAsync(ScrollToSelectedEvent);
        }
        catch (Exception e)
        {
            TraceLogger.Error($"Failed to scroll to selected event: {e}");
        }
    }

    private void RebuildRowIndexMap()
    {
        var displayedEvents = _currentTable?.DisplayedEvents;

        if (ReferenceEquals(displayedEvents, _lastDisplayedEvents)) { return; }

        _lastDisplayedEvents = displayedEvents;
        _rowIndexMap = new(displayedEvents?.Count ?? 0, ReferenceEqualityComparer.Instance);
        // New event-list reference means stored DisplayEventModel instances
        // are stale; clearing prevents memory growth across log reloads.
        _highlightCache.Clear();

        if (displayedEvents is null) { return; }

        for (int i = 0; i < displayedEvents.Count; i++)
        {
            _rowIndexMap[displayedEvents[i]] = i;
        }
    }

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

    private void WarnOnUnknownFilterColors(IEnumerable<FilterModel> filters)
    {
        foreach (var filter in filters)
        {
            if (s_highlightNames.ContainsKey(filter.Color)) { continue; }

            int rawValue = (int)filter.Color;
            bool shouldWarn;

            lock (s_warnedUnknownColors)
            {
                shouldWarn = s_warnedUnknownColors.Add(rawValue);
            }

            if (shouldWarn)
            {
                TraceLogger.Warn(
                    $"Unknown HighlightColor value {rawValue} found in filter set; affected filters will be skipped for highlight resolution.");
            }
        }
    }
}
