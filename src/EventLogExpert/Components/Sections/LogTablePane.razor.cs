// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Logging;
using EventLogExpert.Filtering.Basic;
using EventLogExpert.Filtering.Common;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Filtering.Runtime;
using EventLogExpert.UI.Common.Clipboard;
using EventLogExpert.UI.Common.Display;
using EventLogExpert.UI.EventLog;
using EventLogExpert.UI.FilterPane;
using EventLogExpert.UI.Filters;
using EventLogExpert.UI.LogTable;
using EventLogExpert.UI.Menu;
using EventLogExpert.UI.Settings;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Collections.Immutable;
using FilterMode = EventLogExpert.Filtering.Runtime.FilterMode;

namespace EventLogExpert.Components.Sections;

public sealed partial class LogTablePane
{
    private const int DefaultPageSize = 20;

    private static readonly HashSet<int> s_warnedUnknownColors = [];

    private readonly Dictionary<ResolvedEvent, string?> _highlightCache = new(ReferenceEqualityComparer.Instance);

    private IReadOnlyList<ResolvedEvent> _activeDisplayedEvents = [];
    private SavedFilter[] _activeHighlightFilters = [];
    private IReadOnlyList<ResolvedEvent>? _cachedFilteredCanonical;
    private EventLogId? _cachedFilteredTableId;
    private IReadOnlyList<ResolvedEvent>? _cachedFilteredView;
    private LogView? _currentTable;
    private DotNetObjectReference<LogTablePane>? _dotNetRef;
    private ColumnName[] _enabledColumns = null!;
    private ImmutableList<SavedFilter> _filters = [];
    private int _filtersHighlightKey;
    private bool _focusActiveOnNextRender;
    private string _headerName = string.Empty;
    private IReadOnlyList<ResolvedEvent>? _lastIndexedDisplayedEvents;
    // View-local cursor: the row that is the moving end of a range selection within the
    // current table. May briefly diverge from _selectedEvent during local keyboard nav
    // (advanced before the dispatch round-trip) and after RebuildRowIndexMap rebinds it
    // to the equivalent row in a freshly built DisplayedEvents list. Defaults to the
    // same row as _selectionAnchor for single-row selections.
    private ResolvedEvent? _localCursor;
    private LogTableState _logTableState = null!;
    private int _pageSize = DefaultPageSize;
    private ColumnName[] _previousEnabledColumns = [];
    private bool _resortSelectionOnNextRender;
    private Dictionary<ResolvedEvent, int> _rowIndexMap = new(ReferenceEqualityComparer.Instance);
    private ResolvedEvent? _selectedEvent;
    private ImmutableList<ResolvedEvent> _selectedEvents = [];
    private HashSet<ResolvedEvent> _selectedSet = new(ReferenceEqualityComparer.Instance);
    // The fixed end of a range selection — set on plain click, Ctrl+Click,
    // and any keyboard nav that establishes a single selection. Reused for
    // Shift+Click and Shift+Arrow to compute the range.
    private ResolvedEvent? _selectionAnchor;
    private TimeZoneInfo _timeZoneSettings = null!;

    [Inject] private IClipboardService ClipboardService { get; init; } = null!;

    [Inject] private ILogTableColumnDefaultsProvider ColumnDefaults { get; init; } = null!;

    [Inject] private IState<FilterPaneState> FilterPaneState { get; init; } = null!;

    [Inject] private IEventLogCommands EventLogCommands { get; init; } = null!;

    [Inject] private IFilterPaneCommands FilterPaneCommands { get; init; } = null!;

    [Inject] private IFilterService FilterService { get; init; } = null!;

    [Inject] private IHighlightSelector HighlightSelector { get; init; } = null!;

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    [Inject] private ILogTableCommands LogTableCommands { get; init; } = null!;

    [Inject] private IState<LogTableState> LogTableState { get; init; } = null!;

    [Inject] private IMenuService MenuService { get; init; } = null!;

    [Inject] private IStateSelection<EventLogState, ResolvedEvent?> SelectedEvent { get; init; } = null!;

    [Inject] private IStateSelection<EventLogState, ImmutableList<ResolvedEvent>> SelectedEvents { get; init; } = null!;

    [Inject] private ISettingsService Settings { get; init; } = null!;

    [Inject] private ITraceLogger TraceLogger { get; init; } = null!;

    [JSInvokable]
    public void OnColumnReordered(string columnName, string targetColumn, bool insertAfter)
    {
        if (Enum.TryParse<ColumnName>(columnName, out var column) &&
            Enum.TryParse<ColumnName>(targetColumn, out var target))
        {
            LogTableCommands.ReorderColumn(column, target, insertAfter);
        }
    }

    [JSInvokable]
    public void OnColumnResized(string columnName, int width)
    {
        if (Enum.TryParse<ColumnName>(columnName, out var column))
        {
            LogTableCommands.SetColumnWidth(column, width);
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
            catch (JSDisconnectedException) { /* Circuit gone — JS resource already torn down. */ }

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
            catch (JSDisconnectedException) { /* Circuit gone — fall back to default page size. */ }
            catch (Exception e)
            {
                TraceLogger.Warning($"Failed to measure table page size, using default {DefaultPageSize}: {e}");
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

        SubscribeToAction<SetActiveTableAction>(OnSetActiveTable);
        SubscribeToAction<UpdateDisplayedEventsAction>(_ => RescrollToSelected());
        SubscribeToAction<AppendTableEventsAction>(_ => RescrollToSelected());
        SubscribeToAction<AppendTableEventsBatchAction>(_ => RescrollToSelected());
        SubscribeToAction<UpdateTableAction>(_ => RescrollToSelected());

        _logTableState = LogTableState.Value;

        _currentTable = _logTableState.EventTables.FirstOrDefault(x => x.Id == _logTableState.ActiveEventLogId);
        _enabledColumns = GetOrderedEnabledColumns();
        _selectedEvent = SelectedEvent.Value;
        _localCursor = _selectedEvent;
        _selectedEvents = SelectedEvents.Value;
        _selectedSet = new HashSet<ResolvedEvent>(_selectedEvents, ReferenceEqualityComparer.Instance);
        var initialPaneState = FilterPaneState.Value;
        _filters = initialPaneState.Filters;
        _activeHighlightFilters = HighlightSelector.Select(initialPaneState.Filters);
        _filtersHighlightKey = HighlightSelector.ComputeHighlightKey(initialPaneState.Filters);
        _timeZoneSettings = Settings.TimeZoneInfo;

        WarnOnUnknownFilterColors(_filters);
        RebuildRowIndexMap();

        await base.OnInitializedAsync();
    }

    protected override bool ShouldRender()
    {
        // Snapshot once so the short-circuit check and the Property assignment
        // below see the same reference even if Fluxor publishes a new state
        // mid-method.
        var currentPaneState = FilterPaneState.Value;
        var currentFilters = currentPaneState.Filters;
        bool filtersChanged = !ReferenceEquals(currentFilters, _filters);
        bool selectedEventChanged = !ReferenceEquals(SelectedEvent.Value, _selectedEvent);

        if (ReferenceEquals(LogTableState.Value, _logTableState) &&
            ReferenceEquals(SelectedEvents.Value, _selectedEvents) &&
            !selectedEventChanged &&
            !filtersChanged &&
            Settings.TimeZoneInfo.Equals(_timeZoneSettings)) { return false; }

        bool selectionChanged = !ReferenceEquals(SelectedEvents.Value, _selectedEvents);

        _logTableState = LogTableState.Value;

        _currentTable = _logTableState.EventTables.FirstOrDefault(x => x.Id == _logTableState.ActiveEventLogId);
        _enabledColumns = GetOrderedEnabledColumns();

        if (selectionChanged)
        {
            _selectedEvents = SelectedEvents.Value;
            // Reference equality is intentional. Even though ResolvedEvent is
            // now a fully immutable record (no mutating ResolveXml() workaround),
            // value-equality requires hashing every string Property on every selection
            // mutation. Reference equality keeps selection bookkeeping O(1) and
            // also avoids any chance that two distinct event instances that happen
            // to be value-equal would collapse into a single selected row.
            _selectedSet = new HashSet<ResolvedEvent>(_selectedEvents, ReferenceEqualityComparer.Instance);
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
            int newHighlightKey = HighlightSelector.ComputeHighlightKey(currentFilters);

            if (newHighlightKey != _filtersHighlightKey)
            {
                _filtersHighlightKey = newHighlightKey;
                _activeHighlightFilters = HighlightSelector.Select(currentFilters);
                _highlightCache.Clear();
                WarnOnUnknownFilterColors(_filters);
            }
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

    private static string GetLogShortName(string owningLog) =>
        owningLog[(owningLog.LastIndexOf('\\') + 1)..];

    private static ResolvedEvent? ResolveByKey(
        IReadOnlyList<ResolvedEvent> displayedEvents,
        ResolvedEvent? candidate)
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

    private void ApplySelectedFilter(ResolvedEvent selectedEvent, EventProperty property, bool exclude)
    {
        string filterValue = property switch
        {
            EventProperty.Id => selectedEvent.Id.ToString(),
            EventProperty.ActivityId => selectedEvent.ActivityId?.ToString() ?? string.Empty,
            EventProperty.Level => selectedEvent.Level,
            EventProperty.Keywords => selectedEvent.KeywordsDisplayName,
            EventProperty.Source => selectedEvent.Source,
            EventProperty.TaskCategory => selectedEvent.TaskCategory,
            _ => string.Empty,
        };

        var basicFilter = new BasicFilter(
            new FilterComparison
            {
                Property = property,
                Value = filterValue,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
            },
            []);

        if (!FilterService.TryFormat(basicFilter, out var comparisonString)) { return; }

        var filter = SavedFilter.TryCreate(
            comparisonString,
            basicFilter,
            isExcluded: exclude,
            isEnabled: true,
            mode: FilterMode.Basic);

        if (filter is null) { return; }

        FilterPaneCommands.SetFilter(filter);
    }

    private IReadOnlyList<ResolvedEvent> BuildRange(
        IReadOnlyList<ResolvedEvent> displayedEvents,
        ResolvedEvent anchor,
        ResolvedEvent selected)
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
        var range = new ResolvedEvent[end - start + 1];

        for (int i = 0; i < range.Length; i++)
        {
            range[i] = displayedEvents[start + i];
        }

        return range;
    }

    private void DispatchSetSelection(IReadOnlyList<ResolvedEvent> events, ResolvedEvent? selected)
    {
        // Sort the selection by current row-index for events in this table; events
        // belonging to other open logs (not in _rowIndexMap) preserve their existing
        // relative order at the tail. De-dupe by reference identity throughout so
        // SetSelectedEvents never has to re-process duplicates.
        var seen = new HashSet<ResolvedEvent>(ReferenceEqualityComparer.Instance);
        List<(ResolvedEvent Event, int Index)> inTable = new(events.Count);
        List<ResolvedEvent> outOfTable = [];

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

        var ordered = new List<ResolvedEvent>(inTable.Count + outOfTable.Count);

        foreach (var entry in inTable) { ordered.Add(entry.Event); }

        ordered.AddRange(outOfTable);

        EventLogCommands.SetSelectedEvents(ordered, selected);
    }

    private async Task FocusActiveRow()
    {
        if (_localCursor is null) { return; }

        if (!_rowIndexMap.TryGetValue(_localCursor, out int index)) { return; }

        try
        {
            await JSRuntime.InvokeVoidAsync("focusEventTableRow", index);
        }
        catch (JSDisconnectedException) { /* Circuit gone — focus best-effort during teardown. */ }
        catch (Exception e)
        {
            TraceLogger.Warning($"Failed to focus active table row: {e}");
        }
    }

    private int GetActiveIndex(IReadOnlyList<ResolvedEvent> displayedEvents)
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
        _logTableState.ColumnWidths.TryGetValue(column, out int width) ? width : ColumnDefaults.GetColumnWidth(column);

    private string GetCss(ResolvedEvent @event) =>
        _selectedSet.Contains(@event) ? "table-row selected" : "table-row";

    private string GetDateColumnHeader() =>
        Settings.TimeZoneInfo.Equals(TimeZoneInfo.Local) ?
            "Date and Time" :
            $"Date and Time {Settings.TimeZoneInfo.DisplayName.Split(" ").First()}";

    private string? GetHighlight(ResolvedEvent @event)
    {
        // Selected rows show selection styling (.selected wins via !important);
        // skip cache writes so deselecting doesn't require a refill.
        if (_selectedSet.Contains(@event)) { return null; }

        if (_highlightCache.TryGetValue(@event, out var cached)) { return cached; }

        string? color = null;

        foreach (var filter in _activeHighlightFilters)
        {
            if (!filter.Compiled!.Predicate(@event)) { continue; }

            color = filter.Color.ToCssName();

            break;
        }

        _highlightCache[@event] = color;

        return color;
    }

    private ColumnName[] GetOrderedEnabledColumns()
    {
        var enabledSet = _logTableState.Columns
            .Where(column => column.Value)
            .Select(column => column.Key)
            .ToHashSet();

        if (_logTableState.ColumnOrder.IsEmpty)
        {
            // Use ColumnDefaults.ColumnOrder for a deterministic fallback rather than
            // HashSet iteration order, which is not guaranteed.
            return ColumnDefaults.ColumnOrder.Where(enabledSet.Contains).ToArray();
        }

        return _logTableState.ColumnOrder
            .Where(enabledSet.Contains)
            .ToArray();
    }

    private int GetRowIndex(ResolvedEvent evt) =>
        _rowIndexMap.TryGetValue(evt, out int index) ? index + 2 : 2;

    private async Task HandleKeyDown(KeyboardEventArgs args)
    {
        var displayedEvents = _activeDisplayedEvents;

        if (displayedEvents.Count == 0) { return; }

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

    private void InvokeContextMenu(MouseEventArgs args)
    {
        // Snapshot the currently selected event into the closures so a subsequent selection change
        // (e.g. user clicks elsewhere while the menu is open) doesn't retarget the action. Note:
        // selection follows the right-click in LogTablePane, so SelectedEvent.Value matches the row
        // under the pointer at invocation time.
        var clicked = SelectedEvent.Value;

        if (clicked is null) { return; }

        MenuService.OpenAt(args.ClientX, args.ClientY, ShowContextMenuItems(clicked));
    }

    private void InvokeTableColumnMenu(MouseEventArgs args) =>
        MenuService.OpenAt(args.ClientX, args.ClientY, ShowColumnMenuItems());

    private bool IsSelectionOutOfSortOrder(IReadOnlyList<ResolvedEvent> selection)
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

    private async void OnSetActiveTable(SetActiveTableAction action)
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
        var displayedEvents = ResolveActiveDisplayedEvents();
        _activeDisplayedEvents = displayedEvents;

        if (ReferenceEquals(displayedEvents, _lastIndexedDisplayedEvents)) { return; }

        _lastIndexedDisplayedEvents = displayedEvents;
        _rowIndexMap = new Dictionary<ResolvedEvent, int>(displayedEvents.Count, ReferenceEqualityComparer.Instance);
        // New event-list reference means stored ResolvedEvent instances
        // are stale; clearing prevents memory growth across log reloads.
        _highlightCache.Clear();

        if (displayedEvents.Count == 0)
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

    private async void RescrollToSelected()
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

    private IReadOnlyList<ResolvedEvent> ResolveActiveDisplayedEvents()
    {
        if (_currentTable is null) { return []; }

        if (_currentTable.IsCombined) { return _logTableState.DisplayedEvents; }

        var canonical = _logTableState.DisplayedEvents;

        if (_cachedFilteredView is not null &&
            ReferenceEquals(canonical, _cachedFilteredCanonical) &&
            _cachedFilteredTableId == _currentTable.Id)
        {
            return _cachedFilteredView;
        }

        int expectedCapacity = _logTableState.EventCountByLog.TryGetValue(_currentTable.Id, out int trackedCount)
            ? trackedCount
            : 0;

        var filtered = new List<ResolvedEvent>(expectedCapacity);

        for (int eventIndex = 0; eventIndex < canonical.Count; eventIndex++)
        {
            var current = canonical[eventIndex];

            if (string.Equals(current.OwningLog, _currentTable.LogName, StringComparison.Ordinal))
            {
                filtered.Add(current);
            }
        }

        var result = filtered.AsReadOnly();
        _cachedFilteredCanonical = canonical;
        _cachedFilteredTableId = _currentTable.Id;
        _cachedFilteredView = result;

        return result;
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

        var displayedEvents = _activeDisplayedEvents;

        if (displayedEvents.Count == 0) { return; }

        // Match on OwningLog (the per-source identifier — file path for
        // exported logs, channel name for live logs) in addition to LogName
        // and RecordId so we don't scroll to a value-equal row from a
        // different open log when multiple sources share the same channel
        // name and overlapping record-id ranges. Single pass over the list
        // returns both the row and its index without re-scanning via IndexOf.
        for (var index = 0; index < displayedEvents.Count; index++)
        {
            var candidate = displayedEvents[index];

            if (candidate.RecordId != target.RecordId ||
                !string.Equals(candidate.OwningLog, target.OwningLog, StringComparison.Ordinal) ||
                !string.Equals(candidate.LogName, target.LogName, StringComparison.Ordinal))
            {
                continue;
            }

            await JSRuntime.InvokeVoidAsync("scrollToRow", index);

            return;
        }
    }

    private void SelectEvent(MouseEventArgs args, ResolvedEvent @event)
    {
        var displayedEvents = _activeDisplayedEvents;

        switch (args)
        {
            case { ShiftKey: true } when displayedEvents.Count > 0:
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
                    var merged = new List<ResolvedEvent>(_selectedEvents.Count + range.Count);
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
                    var remaining = new List<ResolvedEvent>(_selectedEvents.Count);

                    foreach (var existingEvent in _selectedEvents)
                    {
                        if (!ReferenceEquals(existingEvent, @event)) { remaining.Add(existingEvent); }
                    }

                    DispatchSetSelection(remaining, @event);
                }
                else
                {
                    var combined = new List<ResolvedEvent>(_selectedEvents.Count + 1);
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

    private IReadOnlyList<MenuItem> ShowColumnMenuItems()
    {
        var state = LogTableState.Value;
        var items = new List<MenuItem>();

        foreach (var (column, isVisible) in state.Columns)
        {
            var capturedColumn = column;
            items.Add(MenuItem.Item(
                column.ToFullString(),
                () => LogTableCommands.ToggleColumn(capturedColumn),
                isChecked: isVisible));
        }

        items.Add(MenuItem.Separator());

        var orderItems = new List<MenuItem>();
        foreach (var (column, _) in state.Columns)
        {
            var capturedColumn = column;
            orderItems.Add(MenuItem.Item(
                column.ToFullString(),
                () => LogTableCommands.SetOrderBy(capturedColumn),
                isChecked: state.OrderBy.Equals(capturedColumn)));
        }

        items.Add(MenuItem.SubMenu("Order By", orderItems));
        items.Add(MenuItem.Separator());
        items.Add(MenuItem.Item(
            "Reset Column Defaults",
            () => LogTableCommands.ResetColumnDefaults()));

        return items;
    }

    private IReadOnlyList<MenuItem> ShowContextMenuItems(ResolvedEvent selectedEvent)
    {
        return
        [
            MenuItem.Item("Copy Selected", () => ClipboardService.CopySelectedEvent(EventCopyFormat.Default)),
            MenuItem.Item("Copy Selected (Simple)", () => ClipboardService.CopySelectedEvent(EventCopyFormat.Simple)),
            MenuItem.Item("Copy Selected (XML)", () => ClipboardService.CopySelectedEvent(EventCopyFormat.Xml)),
            MenuItem.Item("Copy Selected (Full)", () => ClipboardService.CopySelectedEvent(EventCopyFormat.Full)),
            MenuItem.Separator(),
            MenuItem.Item("Exclude Events Before", () =>
                FilterPaneCommands.SetFilterDateRange(
                    new DateFilter { Before = selectedEvent.TimeCreated })),
            MenuItem.Item("Exclude Events After", () =>
                FilterPaneCommands.SetFilterDateRange(
                    new DateFilter { After = selectedEvent.TimeCreated })),
            MenuItem.Separator(),
            MenuItem.SubMenu("Include", ShowEventFieldItems(selectedEvent, false)),
            MenuItem.SubMenu("Exclude", ShowEventFieldItems(selectedEvent, true)),
        ];
    }

    private IReadOnlyList<MenuItem> ShowEventFieldItems(ResolvedEvent selectedEvent, bool exclude)
    {
        var items = new List<MenuItem>();

        foreach (EventProperty property in Enum.GetValues<EventProperty>())
        {
            if (property is EventProperty.Description or EventProperty.Xml) { continue; }

            var capturedProperty = property;
            items.Add(MenuItem.Item(
                property.ToFullString(),
                () => ApplySelectedFilter(selectedEvent, capturedProperty, exclude)));
        }

        return items;
    }

    private void ToggleSorting() => LogTableCommands.ToggleSortDirection();

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
            TraceLogger.Warning($"Failed to refresh table page size: {e}");

            return 0;
        }
    }

    private void WarnOnUnknownFilterColors(IEnumerable<SavedFilter> filters)
    {
        foreach (var filter in filters)
        {
            if (Enum.IsDefined(filter.Color)) { continue; }

            int rawValue = (int)filter.Color;
            bool shouldWarn;

            lock (s_warnedUnknownColors)
            {
                shouldWarn = s_warnedUnknownColors.Add(rawValue);
            }

            if (shouldWarn)
            {
                TraceLogger.Warning(
                    $"Unknown HighlightColor value {rawValue} found in filter set; affected filters will be skipped for highlight resolution.");
            }
        }
    }
}
