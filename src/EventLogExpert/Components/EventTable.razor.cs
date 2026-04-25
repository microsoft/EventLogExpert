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
    private const int DefaultPageSize = 20;

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

    private readonly Dictionary<DisplayEventModel, string?> _highlightCache = new(ReferenceEqualityComparer.Instance);

    private EventTableModel? _currentTable;
    private DotNetObjectReference<EventTable>? _dotNetRef;
    private ColumnName[] _enabledColumns = null!;
    private EventTableState _eventTableState = null!;
    private ImmutableList<FilterModel> _filters = [];
    private bool _focusActiveOnNextRender;
    private string _headerName = string.Empty;
    private IReadOnlyList<DisplayEventModel>? _lastDisplayedEvents;
    // View-local cursor: the row that is the moving end of a range selection within the
    // current table. May briefly diverge from _selectedEvent during local keyboard nav
    // (advanced before the dispatch round-trip) and after RebuildRowIndexMap rebinds it
    // to the equivalent row in a freshly built DisplayedEvents list. Defaults to the
    // same row as _selectionAnchor for single-row selections.
    private DisplayEventModel? _localCursor;
    private int _pageSize = DefaultPageSize;
    private ColumnName[] _previousEnabledColumns = [];
    private bool _resortSelectionOnNextRender;
    private Dictionary<DisplayEventModel, int> _rowIndexMap = new(ReferenceEqualityComparer.Instance);
    private DisplayEventModel? _selectedEvent;
    private ImmutableList<DisplayEventModel> _selectedEvents = [];
    private HashSet<DisplayEventModel> _selectedSet = new(ReferenceEqualityComparer.Instance);
    // The fixed end of a range selection — set on plain click, Ctrl+Click,
    // and any keyboard nav that establishes a single selection. Reused for
    // Shift+Click and Shift+Arrow to compute the range.
    private DisplayEventModel? _selectionAnchor;
    private TimeZoneInfo _timeZoneSettings = null!;

    [Inject] private IClipboardService ClipboardService { get; init; } = null!;

    [Inject] private IDispatcher Dispatcher { get; init; } = null!;

    [Inject] private IState<EventTableState> EventTableState { get; init; } = null!;

    [Inject] private IState<FilterPaneState> FilterPaneState { get; init; } = null!;

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    [Inject] private IStateSelection<EventLogState, DisplayEventModel?> SelectedEvent { get; init; } = null!;

    [Inject] private IStateSelection<EventLogState, ImmutableList<DisplayEventModel>> SelectedEvents { get; init; } = null!;

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

        if (firstRender)
        {
            try
            {
                int measured = await JSRuntime.InvokeAsync<int>("getEventTablePageSize");

                if (measured > 0) { _pageSize = measured; }
            }
            catch (JSDisconnectedException) { }
            catch (Exception e)
            {
                TraceLogger.Warn($"Failed to measure table page size, using default {DefaultPageSize}: {e}");
            }
        }

        if (_focusActiveOnNextRender)
        {
            _focusActiveOnNextRender = false;
            await FocusActiveRow();
        }

        if (_resortSelectionOnNextRender)
        {
            _resortSelectionOnNextRender = false;
            ResortSelectionForCurrentTable();
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override async Task OnInitializedAsync()
    {
        SelectedEvent.Select(s => s.SelectedEvent);
        SelectedEvents.Select(s => s.SelectedEvents);

        SubscribeToAction<EventTableAction.SetActiveTable>(OnSetActiveTable);
        SubscribeToAction<EventTableAction.UpdateCombinedEvents>(OnUpdateCombinedEvents);
        SubscribeToAction<EventTableAction.UpdateDisplayedEvents>(OnUpdateDisplayedEvents);

        _eventTableState = EventTableState.Value;

        _currentTable = _eventTableState.EventTables.FirstOrDefault(x => x.Id == _eventTableState.ActiveEventLogId);
        _enabledColumns = GetOrderedEnabledColumns();
        _selectedEvent = SelectedEvent.Value;
        _localCursor = _selectedEvent;
        _selectedEvents = SelectedEvents.Value;
        _selectedSet = new HashSet<DisplayEventModel>(_selectedEvents, ReferenceEqualityComparer.Instance);
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
        bool selectedEventChanged = !ReferenceEquals(SelectedEvent.Value, _selectedEvent);

        if (ReferenceEquals(EventTableState.Value, _eventTableState) &&
            ReferenceEquals(SelectedEvents.Value, _selectedEvents) &&
            !selectedEventChanged &&
            !filtersChanged &&
            Settings.TimeZoneInfo.Equals(_timeZoneSettings)) { return false; }

        bool selectionChanged = !ReferenceEquals(SelectedEvents.Value, _selectedEvents);

        _eventTableState = EventTableState.Value;

        _currentTable = _eventTableState.EventTables.FirstOrDefault(x => x.Id == _eventTableState.ActiveEventLogId);
        _enabledColumns = GetOrderedEnabledColumns();

        if (selectionChanged)
        {
            _selectedEvents = SelectedEvents.Value;
            // Reference equality is intentional. Even though DisplayEventModel is
            // now a fully immutable record (no mutating ResolveXml() workaround),
            // value-equality requires hashing every string field on every selection
            // mutation. Reference equality keeps selection bookkeeping O(1) and
            // also avoids any chance that two distinct event instances that happen
            // to be value-equal would collapse into a single selected row.
            _selectedSet = new HashSet<DisplayEventModel>(_selectedEvents, ReferenceEqualityComparer.Instance);
        }

        if (selectedEventChanged)
        {
            _selectedEvent = SelectedEvent.Value;
            // Reconcile the local cursor with the store. Without this, store-side
            // updates (reload restore, close-log clearing) would not propagate to
            // the keyboard-nav cursor.
            _localCursor = _selectedEvent;
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

    private static DisplayEventModel? ResolveByKey(
        IReadOnlyList<DisplayEventModel> displayedEvents,
        DisplayEventModel? candidate)
    {
        if (candidate is null) { return null; }

        foreach (var evt in displayedEvents)
        {
            if (ReferenceEquals(evt, candidate)) { return evt; }

            // Skip key-based matching when either side has no RecordId —
            // null == null is true for nullable longs, which would falsely
            // collapse distinct error-read events that share TimeCreated.
            if (evt.RecordId is null || candidate.RecordId is null) { continue; }

            if (evt.RecordId == candidate.RecordId &&
                evt.TimeCreated == candidate.TimeCreated &&
                string.Equals(evt.OwningLog, candidate.OwningLog, StringComparison.Ordinal))
            {
                return evt;
            }
        }

        return null;
    }

    private IReadOnlyList<DisplayEventModel> BuildRange(
        IReadOnlyList<DisplayEventModel> displayedEvents,
        DisplayEventModel anchor,
        DisplayEventModel selected)
    {
        // O(1) lookups via _rowIndexMap; fall back to a linear scan only if
        // the map is stale (e.g., during a render between table rebuilds).
        if (!_rowIndexMap.TryGetValue(anchor, out int anchorIndex)) { anchorIndex = -1; }

        if (!_rowIndexMap.TryGetValue(selected, out int activeIndex)) { activeIndex = -1; }

        if (anchorIndex < 0 || activeIndex < 0)
        {
            for (int i = 0; i < displayedEvents.Count; i++)
            {
                if (anchorIndex < 0 && ReferenceEquals(displayedEvents[i], anchor)) { anchorIndex = i; }

                if (activeIndex < 0 && ReferenceEquals(displayedEvents[i], selected)) { activeIndex = i; }

                if (anchorIndex >= 0 && activeIndex >= 0) { break; }
            }
        }

        if (anchorIndex < 0 || activeIndex < 0) { return [selected]; }

        int start = Math.Min(anchorIndex, activeIndex);
        int end = Math.Max(anchorIndex, activeIndex);
        var range = new DisplayEventModel[end - start + 1];

        for (int i = 0; i < range.Length; i++)
        {
            range[i] = displayedEvents[start + i];
        }

        return range;
    }

    private void DispatchSetSelection(IReadOnlyList<DisplayEventModel> events, DisplayEventModel? selected)
    {
        // Sort the selection by current row-index for events in this table; events
        // belonging to other open logs (not in _rowIndexMap) preserve their existing
        // relative order at the tail. De-dupe by reference identity throughout so
        // SetSelectedEvents never has to re-process duplicates.
        var seen = new HashSet<DisplayEventModel>(ReferenceEqualityComparer.Instance);
        List<(DisplayEventModel Event, int Index)> inTable = new(events.Count);
        List<DisplayEventModel> outOfTable = [];

        foreach (var selectedEvent in events)
        {
            if (!seen.Add(selectedEvent)) { continue; }

            if (_rowIndexMap.TryGetValue(selectedEvent, out int index))
            {
                inTable.Add((selectedEvent, index));
            }
            else
            {
                outOfTable.Add(selectedEvent);
            }
        }

        inTable.Sort(static (left, right) => left.Index.CompareTo(right.Index));

        var ordered = new List<DisplayEventModel>(inTable.Count + outOfTable.Count);

        foreach (var entry in inTable) { ordered.Add(entry.Event); }

        ordered.AddRange(outOfTable);

        Dispatcher.Dispatch(new EventLogAction.SetSelectedEvents(ordered, selected));
    }

    private async Task FocusActiveRow()
    {
        if (_localCursor is null) { return; }

        if (!_rowIndexMap.TryGetValue(_localCursor, out int index)) { return; }

        try
        {
            await JSRuntime.InvokeVoidAsync("focusEventTableRow", index);
        }
        catch (JSDisconnectedException) { }
        catch (Exception e)
        {
            TraceLogger.Warn($"Failed to focus active table row: {e}");
        }
    }

    private int GetActiveIndex(IReadOnlyList<DisplayEventModel> displayedEvents)
    {
        if (_localCursor is not null && _rowIndexMap.TryGetValue(_localCursor, out int idx))
        {
            return idx;
        }

        // Fall back to last selected; without that, anchor on the first row
        // so the first ArrowDown selects the second row, not nothing.
        var fallback = _selectedEvents.LastOrDefault();

        if (fallback is not null && _rowIndexMap.TryGetValue(fallback, out int fallbackIndex))
        {
            return fallbackIndex;
        }

        return displayedEvents.Count > 0 ? 0 : -1;
    }

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
        foreach (var filter in _filters)
        {
            if (filter is not { IsEnabled: true, IsExcluded: false }) { continue; }

            if (filter.Compiled is null || !filter.Compiled.Predicate(@event)) { continue; }

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

    private async Task HandleKeyDown(KeyboardEventArgs args)
    {
        var displayedEvents = _currentTable?.DisplayedEvents;

        if (displayedEvents is null || displayedEvents.Count == 0) { return; }

        // Ctrl+C copies the active selection regardless of focused row.
        if (args is { CtrlKey: true, Code: "KeyC" })
        {
            await ClipboardService.CopySelectedEvent();

            return;
        }

        // Ctrl+A: select all displayed events. Anchor=first, active=last.
        if (args is { CtrlKey: true, Code: "KeyA" })
        {
            _selectionAnchor = displayedEvents[0];
            _localCursor = displayedEvents[^1];
            DispatchSetSelection(displayedEvents, _localCursor);

            return;
        }

        // Escape: clear selection entirely.
        if (args.Code == "Escape")
        {
            _selectionAnchor = null;
            _localCursor = null;
            DispatchSetSelection([], null);

            return;
        }

        // Navigation keys: ArrowUp/Down, Home/End, PageUp/Down.
        int currentIndex = GetActiveIndex(displayedEvents);
        int targetIndex;

        switch (args.Code)
        {
            case "ArrowUp":
                targetIndex = Math.Max(0, currentIndex - 1);
                break;
            case "ArrowDown":
                targetIndex = Math.Min(displayedEvents.Count - 1, currentIndex + 1);
                break;
            case "PageUp":
            case "PageDown":
                // Refresh page size on each press so window/splitter resizes
                // don't leave the cached value stale for the rest of the
                // session.
                int liveStep = await TryRefreshPageSize();
                int step = liveStep > 0 ? liveStep : _pageSize;

                targetIndex = args.Code == "PageUp"
                    ? Math.Max(0, currentIndex - step)
                    : Math.Min(displayedEvents.Count - 1, currentIndex + step);
                break;
            case "Home":
                targetIndex = 0;
                break;
            case "End":
                targetIndex = displayedEvents.Count - 1;
                break;
            default:
                return;
        }

        if (targetIndex == currentIndex && _localCursor is not null) { return; }

        var targetEvent = displayedEvents[targetIndex];

        if (args.ShiftKey)
        {
            // Extend the range from the anchor to the new active row. If we
            // have no anchor (e.g., first navigation into an empty selection),
            // anchor on the previous active or the new target row.
            _selectionAnchor ??= _localCursor ?? targetEvent;
            _localCursor = targetEvent;
            DispatchSetSelection(BuildRange(displayedEvents, _selectionAnchor, targetEvent), targetEvent);
        }
        else
        {
            _selectionAnchor = targetEvent;
            _localCursor = targetEvent;
            DispatchSetSelection([targetEvent], targetEvent);
        }

        _focusActiveOnNextRender = true;
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

    private bool IsSelectionOutOfSortOrder(IReadOnlyList<DisplayEventModel> selection)
    {
        int lastIndex = -1;

        foreach (var selectedEvent in selection)
        {
            if (!_rowIndexMap.TryGetValue(selectedEvent, out int index)) { continue; }

            if (index < lastIndex) { return true; }

            lastIndex = index;
        }

        return false;
    }

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

        if (displayedEvents is null)
        {
            _selectionAnchor = null;
            _localCursor = null;

            return;
        }

        for (int i = 0; i < displayedEvents.Count; i++)
        {
            _rowIndexMap[displayedEvents[i]] = i;
        }

        // Re-resolve anchor/active by stable key when DisplayedEvents has
        // been replaced (sort/filter/reload). Reference equality alone would
        // drop them on every list rebuild even when the same logical event
        // is still visible.
        _selectionAnchor = ResolveByKey(displayedEvents, _selectionAnchor);
        _localCursor = ResolveByKey(displayedEvents, _localCursor);

        // Detect when the existing selection no longer matches sort order so
        // OnAfterRenderAsync can dispatch a re-sorted SetSelectedEvents.
        // Events outside the current displayed table preserve their relative
        // position and never trigger a re-sort by themselves.
        if (IsSelectionOutOfSortOrder(_selectedEvents))
        {
            _resortSelectionOnNextRender = true;
        }
    }

    private void ResortSelectionForCurrentTable()
    {
        // Re-publish the current selection in the new sort order. The dispatch
        // is idempotent — ReduceSetSelectedEvents short-circuits when both the
        // selection and active event are unchanged by reference, so this is
        // safe to call after every DisplayedEvents reference change.
        DispatchSetSelection(_selectedEvents, _localCursor ?? _selectedEvent);
    }

    private async Task ScrollToSelectedEvent()
    {
        // Target the active event (focused row) rather than the last selected
        // event — selection is now in sort order, so "last in selection" no
        // longer corresponds to "the row the user is interacting with".
        var target = _localCursor ?? _selectedEvent ?? _selectedEvents.LastOrDefault();

        if (target is null) { return; }

        // Match on OwningLog (the per-source identifier — file path for
        // exported logs, channel name for live logs) in addition to LogName
        // and RecordId so we don't scroll to a value-equal row from a
        // different open log when multiple sources share the same channel
        // name and overlapping record-id ranges.
        var entry = _currentTable?.DisplayedEvents.FirstOrDefault(x =>
            string.Equals(x.OwningLog, target.OwningLog, StringComparison.Ordinal) &&
            string.Equals(x.LogName, target.LogName, StringComparison.Ordinal) &&
            x.RecordId == target.RecordId);

        if (entry is null) { return; }

        var index = _currentTable?.DisplayedEvents.IndexOf(entry);

        if (index >= 0)
        {
            await JSRuntime.InvokeVoidAsync("scrollToRow", index);
        }
    }

    private void SelectEvent(MouseEventArgs args, DisplayEventModel @event)
    {
        var displayedEvents = _currentTable?.DisplayedEvents;

        switch (args)
        {
            case { ShiftKey: true } when displayedEvents is not null:
                // Shift+Click: range from anchor to clicked. Anchor stays put.
                // Without an anchor (first interaction), treat as a plain
                // click so we have something to extend from on the next click.
                if (_selectionAnchor is null)
                {
                    _selectionAnchor = @event;
                    _localCursor = @event;
                    DispatchSetSelection([@event], @event);

                    return;
                }

                _localCursor = @event;
                var range = BuildRange(displayedEvents, _selectionAnchor, @event);

                if (args.CtrlKey)
                {
                    // Ctrl+Shift+Click: additive range. Merge existing
                    // selection with the new range. Dedupe by reference is
                    // handled centrally inside DispatchSetSelection.
                    var merged = new List<DisplayEventModel>(_selectedEvents.Count + range.Count);
                    merged.AddRange(_selectedEvents);
                    merged.AddRange(range);
                    DispatchSetSelection(merged, @event);
                }
                else
                {
                    DispatchSetSelection(range, @event);
                }

                return;

            case { CtrlKey: true }:
                // Ctrl+Click toggles a single row and moves the anchor to it.
                // Active stays on the clicked row even if it was deselected
                // (Explorer-style focus semantics).
                _selectionAnchor = @event;
                _localCursor = @event;

                if (_selectedSet.Contains(@event))
                {
                    var remaining = new List<DisplayEventModel>(_selectedEvents.Count);

                    foreach (var existingEvent in _selectedEvents)
                    {
                        if (!ReferenceEquals(existingEvent, @event)) { remaining.Add(existingEvent); }
                    }

                    DispatchSetSelection(remaining, @event);
                }
                else
                {
                    var combined = new List<DisplayEventModel>(_selectedEvents.Count + 1);
                    combined.AddRange(_selectedEvents);
                    combined.Add(@event);
                    DispatchSetSelection(combined, @event);
                }

                return;

            default:
                // Right-click on a row that's already part of a multi-selection
                // should preserve the selection and only move focus to the
                // clicked row, matching Windows Explorer behavior so the
                // context menu can act on the existing selection. Left/middle
                // clicks (and right-clicks on a non-selected row) replace the
                // selection with just the clicked row.
                if (args.Button == 2 && _selectedSet.Contains(@event))
                {
                    _localCursor = @event;
                    DispatchSetSelection(_selectedEvents, @event);

                    return;
                }

                _selectionAnchor = @event;
                _localCursor = @event;
                DispatchSetSelection([@event], @event);

                return;
        }
    }

    private void ToggleSorting() => Dispatcher.Dispatch(new EventTableAction.ToggleSorting());

    private async Task<int> TryRefreshPageSize()
    {
        try
        {
            int measured = await JSRuntime.InvokeAsync<int>("getEventTablePageSize");

            if (measured > 0) { _pageSize = measured; }

            return measured;
        }
        catch (JSDisconnectedException) { return 0; }
        catch (Exception e)
        {
            TraceLogger.Warn($"Failed to refresh table page size: {e}");

            return 0;
        }
    }

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
