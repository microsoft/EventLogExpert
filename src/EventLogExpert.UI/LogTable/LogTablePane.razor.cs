// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Common.Filtering;
using EventLogExpert.Filtering.Compilation;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.Common.Display;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.UI.Common.Interop;
using EventLogExpert.UI.LogTable.Grouping;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using Microsoft.JSInterop;
using System.Collections.Immutable;

namespace EventLogExpert.UI.LogTable;

public sealed partial class LogTablePane
{
    private const int DefaultPageSize = 20;
    // Must match the CSS `tr { height: 22px }`; Virtualize defaults ItemSize to 50px, so leaving it unset computes the
    // scroll model against a 50px spacer and jump-to-selected lands at the wrong position. Set explicitly so the model
    // matches from the first render.
    private const float EventRowHeightPixels = 22f;
    private const int MenuValueMaxLength = 40;
    private const string NoCellValueReason = "No value in this cell to filter on";

    private static readonly IEventColumnView s_emptyView = Runtime.LogTable.LogTableState.EmptyView;
    private static readonly HashSet<int> s_warnedUnknownColors = [];

    // Keyed on EventLocator (physical, generation-stamped) so the cache survives a re-sort within a generation; default
    // value equality (a ReferenceEqualityComparer would box every struct key and never match).
    private readonly Dictionary<EventLocator, string?> _highlightCache = [];

    private IEventColumnView _activeDisplayedEvents = s_emptyView;
    private SavedFilter[] _activeHighlightFilters = [];
    private LogView? _currentTable;
    private TableCursor? _cursor;
    private DotNetObjectReference<LogTablePane>? _dotNetRef;
    private ColumnName[] _enabledColumns = null!;
    private Virtualize<DisplayRow>? _eventVirtualize;
    private ImmutableList<SavedFilter> _filters = [];
    private int _filtersHighlightKey;
    private SelectionEntry? _focus;
    private bool _focusActiveOnNextRender;
    private string _headerName = string.Empty;
    private EventLogId? _highlightCacheTableId;
    private IEventColumnView? _lastIndexedDisplayedEvents;
    private LogTableState _logTableState = null!;
    private int _pageSize = DefaultPageSize;
    private ColumnName[] _previousEnabledColumns = [];
    private bool _refreshEventViewportOnRender;
    private bool _repaintViewportOnNextRender;
    private bool _rescrollToSelectedOnRender;
    private bool _resortSelectionOnNextRender;
    private GroupedRowView? _rowView;
    private (IEventColumnView View, EventLogId? TableId, ColumnName? GroupBy, bool GroupDescending, bool CollapsedDefault, ImmutableHashSet<string>? Overrides) _rowViewSnapshot;
    private HashSet<EventLocator> _selectedSet = [];
    private ImmutableList<SelectionEntry> _selection = [];
    private EventLocator? _selectionAnchor;
    private IJSObjectReference? _tableModule;
    private TimeZoneInfo _timeZoneSettings = null!;

    private EventLocator? ActiveHandle =>
        _cursor is { Kind: TableRowKind.Event, Handle: { } handle } ? handle : null;

    [Inject] private IClipboardService ClipboardService { get; init; } = null!;

    [Inject] private ILogTableColumnDefaultsProvider ColumnDefaults { get; init; } = null!;

    [Inject] private IEventLogCommands EventLogCommands { get; init; } = null!;

    [Inject] private IFilterLensCommands FilterLensCommands { get; init; } = null!;

    [Inject] private IFilterPaneCommands FilterPaneCommands { get; init; } = null!;

    [Inject] private IState<FilterPaneState> FilterPaneState { get; init; } = null!;

    [Inject] private IFilterService FilterService { get; init; } = null!;

    [Inject] private IStateSelection<EventLogState, SelectionEntry?> Focus { get; init; } = null!;

    [Inject] private IHighlightSelector HighlightSelector { get; init; } = null!;

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    [Inject] private ILogTableCommands LogTableCommands { get; init; } = null!;

    [Inject] private IState<LogTableState> LogTableState { get; init; } = null!;

    [Inject] private IMenuService MenuService { get; init; } = null!;

    [Inject] private IStateSelection<EventLogState, ImmutableList<SelectionEntry>> Selection { get; init; } = null!;

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

    // Extracted from the ItemsProvider so the viewport clamp (empty view, start past the end, window overrunning the
    // tail) is unit-testable without driving the Virtualize component.
    internal static ItemsProviderResult<DisplayRow> ComputeEventViewport(
        IEventColumnView displayedEvents,
        ItemsProviderRequest request)
    {
        int totalCount = displayedEvents.Count;
        int start = Math.Min(request.StartIndex, totalCount);
        int count = Math.Min(request.Count, totalCount - start);

        IReadOnlyList<DisplayRow> window =
            count <= 0 ? [] : displayedEvents.Slice(start, count);

        return new ItemsProviderResult<DisplayRow>(window, totalCount);
    }

    internal static string GetLevelClass(string level) =>
        LevelSeverity.FromLevelName(level) switch
        {
            SeverityLevel.Error => "bi bi-exclamation-circle error",
            SeverityLevel.Warning => "bi bi-exclamation-triangle warning",
            SeverityLevel.Information => "bi bi-info-circle",
            _ => string.Empty,
        };

    protected override async ValueTask DisposeAsyncCore(bool disposing)
    {
        if (disposing)
        {
            DisposeFind();

            await JsModuleInterop.DisposeModuleSafelyAsync(
                _tableModule,
                static module => module.InvokeVoidAsync("disposeTableEvents"));

            _tableModule = null;

            _dotNetRef?.Dispose();
        }

        await base.DisposeAsyncCore(disposing);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Capture up front and clear: consuming the request after the awaits below could otherwise steal a request
        // that a background action (e.g. a live-tail append) queued during one of those awaits, dropping that later
        // render's scroll. A request arriving mid-render stays set and is handled by the next render instead.
        bool rescrollRequested = _rescrollToSelectedOnRender;
        _rescrollToSelectedOnRender = false;

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
                int measured = _tableModule is not null ? await _tableModule.InvokeAsync<int>("getEventTablePageSize") : 0;

                if (measured > 0) { _pageSize = measured; }
            }
            catch (JSDisconnectedException) { /* Circuit gone; fall back to default page size. */ }
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

        if (_refreshEventViewportOnRender)
        {
            _refreshEventViewportOnRender = false;

            if (_rowView is null && _eventVirtualize is not null)
            {
                try
                {
                    await _eventVirtualize.RefreshDataAsync();
                    _repaintViewportOnNextRender = true;
                    StateHasChanged();
                }
                catch (JSDisconnectedException) { /* Circuit gone; nothing to refresh. */ }
                catch (Exception e)
                {
                    TraceLogger.Error($"Failed to refresh the event viewport: {e}");
                }
            }
        }

        if (rescrollRequested)
        {
            try
            {
                await ScrollToSelectedEvent();
            }
            catch (JSDisconnectedException) { /* Circuit gone; nothing to scroll. */ }
            catch (Exception e)
            {
                TraceLogger.Error($"Failed to scroll to selected event: {e}");
            }
        }

        if (_findScrollToCurrentOnRender)
        {
            try
            {
                await ScrollToCurrentFindMatchAsync();
            }
            catch (JSDisconnectedException) { /* Circuit gone; nothing to scroll. */ _findScrollToCurrentOnRender = false; }
            catch (Exception e)
            {
                _findScrollToCurrentOnRender = false;
                TraceLogger.Error($"{nameof(LogTablePane)}: failed to scroll to find match: {e}");
            }
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override async Task OnInitializedAsync()
    {
        Focus.Select(s => s.Focus);
        Selection.Select(s => s.Selection);

        SubscribeToAction<SetActiveTableAction>(_ => RescrollToSelected());
        SubscribeToAction<DisplayReadyAction>(_ => RescrollToSelected());
        SubscribeToAction<AppendTableEventsAction>(_ => RescrollToSelected());
        SubscribeToAction<AppendTableEventsBatchAction>(_ => RescrollToSelected());
        SubscribeToAction<UpdateTableAction>(_ => RescrollToSelected());

        // A bulk expand/collapse (any trigger) is a user choice: Find relinquishes group-expansion ownership so it won't later undo the user's collapse.
        SubscribeToAction<SetAllGroupsCollapsedAction>(_ => RelinquishFindGroupOwnership());

        _logTableState = LogTableState.Value;

        _currentTable = _logTableState.EventTables.FirstOrDefault(x => x.Id == _logTableState.ActiveEventLogId);
        _enabledColumns = GetOrderedEnabledColumns();
        _focus = Focus.Value;
        SetCursorEvent(_focus?.CurrentHandle);
        _selection = Selection.Value;
        _selectedSet = BuildSelectedSet(_selection);
        var initialPaneState = FilterPaneState.Value;
        _filters = initialPaneState.Filters;
        _activeHighlightFilters = HighlightSelector.Select(initialPaneState.Filters);
        _filtersHighlightKey = HighlightSelector.ComputeHighlightKey(initialPaneState.Filters);
        _timeZoneSettings = Settings.TimeZoneInfo;

        WarnOnUnknownFilterColors(_filters);

        RebuildRowMaps();

        RegisterFind();

        await base.OnInitializedAsync();
    }

    protected override bool ShouldRender()
    {
        var currentPaneState = FilterPaneState.Value;
        var currentFilters = currentPaneState.Filters;
        bool filtersChanged = !ReferenceEquals(currentFilters, _filters);
        // The focus is now a value-type SelectionEntry?; compare by OriginHandle (its stable identity). Reference
        // equality would box and always differ, and full-value equality would re-render on a CurrentHandle re-point.
        bool focusChanged = Focus.Value?.OriginHandle != _focus?.OriginHandle;

        // Also render for a pending focus move or a post-refresh viewport repaint, neither of which changes Fluxor state.
        if (!_focusActiveOnNextRender &&
            !_repaintViewportOnNextRender &&
            !_rescrollToSelectedOnRender &&
            !_findRenderRequested &&
            ReferenceEquals(LogTableState.Value, _logTableState) &&
            ReferenceEquals(Selection.Value, _selection) &&
            !focusChanged &&
            !filtersChanged &&
            Settings.TimeZoneInfo.Equals(_timeZoneSettings)) { return false; }

        _repaintViewportOnNextRender = false;
        _findRenderRequested = false;

        bool selectionChanged = !ReferenceEquals(Selection.Value, _selection);

        _logTableState = LogTableState.Value;

        _currentTable = _logTableState.EventTables.FirstOrDefault(x => x.Id == _logTableState.ActiveEventLogId);
        var previousColumnsForFind = _enabledColumns;
        _enabledColumns = GetOrderedEnabledColumns();
        bool findSearchTextChanged = _findOpen && !previousColumnsForFind.SequenceEqual(_enabledColumns);

        if (selectionChanged)
        {
            _selection = Selection.Value;
            _selectedSet = BuildSelectedSet(_selection);
        }

        if (focusChanged)
        {
            _focus = Focus.Value;
            SetCursorEvent(_focus?.CurrentHandle);
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

        findSearchTextChanged |= _findOpen && !Settings.TimeZoneInfo.Equals(_timeZoneSettings);
        _timeZoneSettings = Settings.TimeZoneInfo;

        RebuildRowMaps();

        if (findSearchTextChanged) { NotifyFindViewChanged(); }

        return true;
    }

    private static HashSet<EventLocator> BuildSelectedSet(ImmutableList<SelectionEntry> selection)
    {
        // Membership tests a rendered row's live locator, so key on CurrentHandle (where the selection currently
        // resolves), not OriginHandle; entries absent from the live generation carry a null CurrentHandle.
        var set = new HashSet<EventLocator>(selection.Count);

        foreach (var entry in selection)
        {
            if (entry.CurrentHandle is { } handle) { set.Add(handle); }
        }

        return set;
    }

    // OriginHandle and CurrentHandle are the same live locator at selection time; ReloadKey re-points the selection
    // after a reload (null for a null-RecordId row, which cannot be re-resolved).
    private static SelectionEntry EntryFor(DisplayRow row) =>
        new(row.Loc, row.Loc, ValueKey.TryCreate(row.Lean, out var key) ? key : null);

    private static string TruncateForMenu(string value)
    {
        string collapsed = value.ReplaceLineEndings(" ");

        if (collapsed.Length <= MenuValueMaxLength) { return collapsed; }

        int limit = char.IsHighSurrogate(collapsed[MenuValueMaxLength - 1])
            ? MenuValueMaxLength - 1
            : MenuValueMaxLength;

        return collapsed[..limit] + "...";
    }

    private void AppendCellFilterItems(List<MenuItem> items, ResolvedEvent @event, ColumnName? column)
    {
        if (column is not { } cellColumn) { return; }

        if (CellFilterBuilder.MapColumn(cellColumn) is not { } property) { return; }

        string columnLabel = cellColumn.ToFullString();

        if (CellFilterBuilder.TryGetDisplayValue(@event, property, out var value))
        {
            string shown = TruncateForMenu(value);
            string verb = property is EventProperty.Keywords ? "has" : "=";

            items.Add(MenuItem.Item(
                $"Include only where {columnLabel} {verb} '{shown}'",
                () => ApplySelectedFilter(@event, property, exclude: false)));
            items.Add(MenuItem.Item(
                $"Exclude where {columnLabel} {verb} '{shown}'",
                () => ApplySelectedFilter(@event, property, exclude: true)));
        }
        else
        {
            items.Add(MenuItem.Item(
                $"Include only where {columnLabel}",
                () => { },
                isEnabled: false,
                disabledReason: NoCellValueReason));
            items.Add(MenuItem.Item(
                $"Exclude where {columnLabel}",
                () => { },
                isEnabled: false,
                disabledReason: NoCellValueReason));
        }

        items.Add(MenuItem.Separator());
    }

    private void ApplyNavSelection(EventLocator target, bool shift)
    {
        if (shift)
        {
            _selectionAnchor ??= ActiveHandle ?? target;
            SetCursorEvent(target);
            DispatchSetSelection(BuildRange(_selectionAnchor.Value, target), target, alreadyOrdered: true);
        }
        else
        {
            _selectionAnchor = target;
            SetCursorEvent(target);
            DispatchSetSelection([EntryFor(target)], target);
        }
    }

    private void ApplySelectedFilter(ResolvedEvent selectedEvent, EventProperty property, bool exclude)
    {
        if (CellFilterBuilder.TryBuild(selectedEvent, property, exclude, out var filter))
        {
            FilterPaneCommands.SetFilter(filter);
        }
    }

    private IReadOnlyList<SelectionEntry> BuildRange(EventLocator anchor, EventLocator selected)
    {
        int anchorIndex = RowIndexOf(anchor);
        int activeIndex = RowIndexOf(selected);

        if (anchorIndex < 0 || activeIndex < 0) { return [EntryFor(selected)]; }

        int start = Math.Min(anchorIndex, activeIndex);
        int end = Math.Max(anchorIndex, activeIndex);

        var slice = _activeDisplayedEvents.Slice(start, end - start + 1);
        var range = new List<SelectionEntry>(slice.Count);

        foreach (var row in slice) { range.Add(EntryFor(row)); }

        return range;
    }

    private void DispatchSetSelection(IReadOnlyList<SelectionEntry> entries, EventLocator? focus, bool alreadyOrdered = false)
    {
        SelectionEntry? focusEntry = focus is { } focusLocator ? EntryFor(focusLocator) : null;

        // These callers pass ordered, unique entries; skip rank + sort.
        if (alreadyOrdered)
        {
            EventLogCommands.SetSelectedEvents(entries, focusEntry);

            return;
        }

        var seen = new HashSet<EventLocator>();
        List<(SelectionEntry Entry, int Index)> inTable = new(entries.Count);
        List<SelectionEntry> outOfTable = [];

        foreach (var entry in entries)
        {
            // Dedupe by OriginHandle (the selection's stable identity); order by the live CurrentHandle position.
            if (!seen.Add(entry.OriginHandle)) { continue; }

            int index = entry.CurrentHandle is { } handle ? RowIndexOf(handle) : -1;

            if (index >= 0)
            {
                inTable.Add((entry, index));
            }
            else
            {
                outOfTable.Add(entry);
            }
        }

        inTable.Sort(static (left, right) => left.Index.CompareTo(right.Index));

        var ordered = new List<SelectionEntry>(inTable.Count + outOfTable.Count);

        foreach (var entry in inTable) { ordered.Add(entry.Entry); }

        ordered.AddRange(outOfTable);

        EventLogCommands.SetSelectedEvents(ordered, focusEntry);
    }

    // Builds a selection entry from a bare locator; rehydrates the lean row to mint the ReloadKey.
    private SelectionEntry EntryFor(EventLocator locator) =>
        new(
            locator,
            locator,
            ValueKey.TryCreate(_activeDisplayedEvents.GetDetailLean(locator), out var key) ? key : null);

    private async Task FocusActiveRow()
    {
        int visibleRow = ResolveCursorVisibleRow();

        try
        {
            if (_tableModule is null) { return; }

            // No cursor row to land on (e.g. closing Find over an empty table): focus the scroll container so keyboard nav isn't stranded on a removed element.
            if (visibleRow < 0)
            {
                await _tableModule.InvokeVoidAsync("focusTableContainer");

                return;
            }

            await _tableModule.InvokeVoidAsync("focusEventTableRow", visibleRow);
        }
        catch (JSDisconnectedException) { /* Circuit gone; focus best-effort during teardown. */ }
        catch (Exception e)
        {
            TraceLogger.Warning($"Failed to focus active table row: {e}");
        }
    }

    private int GetAriaRowCount() => (_rowView?.Count ?? _activeDisplayedEvents.Count) + 1;

    private int GetColumnWidth(ColumnName column) =>
        _logTableState.ColumnWidths.TryGetValue(column, out int width) ? width : ColumnDefaults.GetColumnWidth(column);

    private string GetCss(EventLocator loc) =>
        _selectedSet.Contains(loc) ? "table-row selected" : "table-row";

    private int GetCurrentVisibleRow(IEventColumnView displayedEvents)
    {
        int cursorRow = ResolveCursorVisibleRow();

        if (cursorRow >= 0) { return cursorRow; }

        if (_selection.Count > 0 && _selection[^1].CurrentHandle is { } fallback)
        {
            int fallbackIndex = RowIndexOf(fallback);

            if (fallbackIndex >= 0)
            {
                return _rowView?.VisibleRowForEvent(fallbackIndex) ?? fallbackIndex;
            }
        }

        int count = _rowView?.Count ?? displayedEvents.Count;

        return count > 0 ? 0 : -1;
    }

    private string GetDateColumnHeader() =>
        EventTableColumnFormatter.GetColumnHeader(ColumnName.DateAndTime, Settings.TimeZoneInfo);

    private string GetGroupName() => _logTableState.GroupBy?.ToFullString() ?? string.Empty;

    private string GetGroupValueText(EventGroup group)
    {
        if (group.EventCount == 0) { return "(none)"; }

        // M2: rehydrate the representative row (lean suffices for these header columns) and run the timezone/culture
        // switch. Do NOT read GroupKeyAt here: its canonical key text is not the display header text.
        var representative = _activeDisplayedEvents.GetDetailLean(_activeDisplayedEvents.LocatorAt(group.StartIndex));

        string? value = _logTableState.GroupBy switch
        {
            ColumnName.RecordId => representative.RecordId?.ToString(),
            ColumnName.Level => representative.Level,
            ColumnName.DateAndTime => representative.TimeCreated.ConvertTimeZone(_timeZoneSettings).ToString(),
            ColumnName.ActivityId => representative.ActivityId?.ToString(),
            ColumnName.Log => representative.LogName,
            ColumnName.ComputerName => representative.ComputerName,
            ColumnName.Source => representative.Source,
            ColumnName.EventId => representative.Id.ToString(),
            ColumnName.TaskCategory => representative.TaskCategory,
            ColumnName.Keywords => representative.KeywordsDisplayName,
            ColumnName.ProcessId => representative.ProcessId?.ToString(),
            ColumnName.ThreadId => representative.ThreadId?.ToString(),
            ColumnName.User => representative.UserId?.ToString(),
            _ => null
        };

        return string.IsNullOrEmpty(value) ? "(none)" : value;
    }

    private string? GetHighlight(DisplayRow row)
    {
        if (_selectedSet.Contains(row.Loc)) { return null; }

        if (_highlightCache.TryGetValue(row.Loc, out var cached)) { return cached; }

        if (_activeHighlightFilters.Length == 0)
        {
            _highlightCache[row.Loc] = null;

            return null;
        }

        string? color = null;
        // Highlight filters can reference EventData, so evaluate against the full rehydrated event; the result is
        // cached per locator (stable within a generation) so the rehydrate happens at most once per physical row.
        var detail = _activeDisplayedEvents.GetDetail(row.Loc);

        foreach (var filter in _activeHighlightFilters)
        {
            if (!filter.Compiled!.Predicate(detail)) { continue; }

            color = filter.Color.ToCssName();

            break;
        }

        _highlightCache[row.Loc] = color;

        return color;
    }

    private ColumnName[] GetOrderedEnabledColumns() =>
        [.. _logTableState.GetOrderedEnabledColumns(ColumnDefaults)];

    private int GetRowIndex(EventLocator loc)
    {
        int index = RowIndexOf(loc);

        if (index < 0) { return 2; }

        return (_rowView?.VisibleRowForEvent(index) ?? index) + 2;
    }

    private int GetRowStripe(EventLocator loc)
    {
        int index = RowIndexOf(loc);

        if (index < 0) { return 0; }

        if (_rowView is null) { return index % 2; }

        return (index - _rowView.GroupForEvent(index).StartIndex) % 2;
    }

    private async Task HandleKeyDown(KeyboardEventArgs args)
    {
        // Esc closes an open Find here BEFORE the selection-clearing Escape branch below, so closing Find never wipes the user's selection.
        if (_findOpen && args.Code == "Escape")
        {
            await CloseFind();

            return;
        }

        var displayedEvents = _activeDisplayedEvents;

        if (displayedEvents.Count == 0) { return; }

        if (args is { CtrlKey: true, Code: "KeyC" })
        {
            await ClipboardService.CopySelectedEvent();

            return;
        }

        if (args is { CtrlKey: true, Code: "KeyA" })
        {
            int total = displayedEvents.Count;
            var lastLocator = displayedEvents.LocatorAt(total - 1);
            _selectionAnchor = displayedEvents.LocatorAt(0);
            SetCursorEvent(lastLocator);

            var allRows = displayedEvents.Slice(0, total);
            var allEntries = new List<SelectionEntry>(allRows.Count);

            foreach (var row in allRows) { allEntries.Add(EntryFor(row)); }

            DispatchSetSelection(allEntries, lastLocator, alreadyOrdered: true);

            return;
        }

        if (args.Code == "Escape")
        {
            _selectionAnchor = null;
            SetCursor(null);
            DispatchSetSelection([], null);

            return;
        }

        if (_rowView is not null)
        {
            if (args.Code is "ArrowLeft")
            {
                HandleTreegridLeft();

                return;
            }

            if (args.Code is "ArrowRight")
            {
                HandleTreegridRight();

                return;
            }

            if (args.Key is "Enter")
            {
                if (_cursor is { Kind: TableRowKind.Header, GroupKey: { } enterKey })
                {
                    ToggleGroupCollapsed(enterKey);
                    _focusActiveOnNextRender = true;
                }

                return;
            }
        }

        int count = _rowView?.Count ?? displayedEvents.Count;
        int currentRow = GetCurrentVisibleRow(displayedEvents);
        int targetRow;
        int scanDirection;

        switch (args.Code)
        {
            case "ArrowUp":
                targetRow = Math.Max(0, currentRow - 1);
                scanDirection = -1;
                break;
            case "ArrowDown":
                targetRow = Math.Min(count - 1, currentRow + 1);
                scanDirection = 1;
                break;
            case "PageUp":
            case "PageDown":
                int liveStep = await TryRefreshPageSize();
                int step = liveStep > 0 ? liveStep : _pageSize;

                if (args.Code == "PageUp")
                {
                    targetRow = Math.Max(0, currentRow - step);
                    scanDirection = -1;
                }
                else
                {
                    targetRow = Math.Min(count - 1, currentRow + step);
                    scanDirection = 1;
                }

                break;
            case "Home":
                targetRow = 0;
                scanDirection = 1;
                break;
            case "End":
                targetRow = count - 1;
                scanDirection = -1;
                break;
            default:
                return;
        }

        if (targetRow == currentRow && _cursor is not null) { return; }

        if (_rowView is null)
        {
            ApplyNavSelection(displayedEvents.LocatorAt(targetRow), args.ShiftKey);
            _focusActiveOnNextRender = true;

            return;
        }

        NavigateGroupedTo(targetRow, scanDirection, args.ShiftKey);
    }

    private void HandleTreegridLeft()
    {
        var view = _rowView;

        if (view is null) { return; }

        if (_cursor is { Kind: TableRowKind.Event, Handle: { } handle })
        {
            int index = RowIndexOf(handle);

            if (index >= 0)
            {
                SetCursorHeader(view.GroupForEvent(index).Key);
                _focusActiveOnNextRender = true;

                return;
            }
        }

        if (_cursor is { Kind: TableRowKind.Header, GroupKey: { } key } &&
            view.TryGetGroupByKey(key, out var group) && !group.IsCollapsed)
        {
            ToggleGroupCollapsed(key);
            _focusActiveOnNextRender = true;
        }
    }

    private void HandleTreegridRight()
    {
        var view = _rowView;

        if (view is null ||
            _cursor is not { Kind: TableRowKind.Header, GroupKey: { } key } ||
            !view.TryGetGroupByKey(key, out var group))
        {
            return;
        }

        if (group.IsCollapsed)
        {
            ToggleGroupCollapsed(key);
            _focusActiveOnNextRender = true;

            return;
        }

        if (group.EventCount > 0)
        {
            var firstLocator = _activeDisplayedEvents.LocatorAt(group.StartIndex);
            _selectionAnchor = firstLocator;
            SetCursorEvent(firstLocator);
            DispatchSetSelection([EntryFor(firstLocator)], firstLocator);
            _focusActiveOnNextRender = true;
        }
    }

    private async Task InitializeTableEventHandlers()
    {
        _dotNetRef?.Dispose();
        _dotNetRef = DotNetObjectReference.Create(this);

        _tableModule ??= await JSRuntime.InvokeAsync<IJSObjectReference>(
            "import",
            "./_content/EventLogExpert.UI/LogTable/LogTablePane.razor.js");

        await _tableModule.InvokeVoidAsync("initializeTableEvents", _dotNetRef);
    }

    private void InvokeCellContextMenu(MouseEventArgs args, DisplayRow row, ColumnName? column)
    {
        var items = new List<MenuItem>();
        var detail = _activeDisplayedEvents.GetDetail(row.Loc);

        AppendCellFilterItems(items, detail, column);
        items.AddRange(ShowContextMenuItems(detail));

        MenuService.OpenAt(args.ClientX, args.ClientY, items);
    }

    private void InvokeContextMenu(MouseEventArgs args)
    {
        if (Focus.Value?.CurrentHandle is not { } handle) { return; }

        if (!_activeDisplayedEvents.TryGetDetail(handle, out var clicked)) { return; }

        MenuService.OpenAt(args.ClientX, args.ClientY, ShowContextMenuItems(clicked));
    }

    private void InvokeGroupContextMenu(MouseEventArgs args, EventGroup group)
    {
        SetCursorHeader(group.Key);
        MenuService.OpenAt(args.ClientX, args.ClientY, ShowGroupContextMenuItems(group));
    }

    private void InvokeTableColumnMenu(MouseEventArgs args) =>
        MenuService.OpenAt(args.ClientX, args.ClientY, ShowColumnMenuItems());

    private bool IsSelectionOutOfSortOrder(IReadOnlyList<SelectionEntry> selection)
    {
        int lastIndex = -1;

        foreach (var entry in selection)
        {
            int index = entry.CurrentHandle is { } handle ? RowIndexOf(handle) : -1;

            if (index < 0) { continue; }

            if (index < lastIndex) { return true; }

            lastIndex = index;
        }

        return false;
    }

    private ValueTask<ItemsProviderResult<DisplayRow>> LoadEventViewport(ItemsProviderRequest request) =>
        ValueTask.FromResult(ComputeEventViewport(_activeDisplayedEvents, request));

    private void NavigateGroupedTo(
        int targetRow,
        int scanDirection,
        bool shift)
    {
        var view = _rowView!;
        var row = view[targetRow];

        if (row.Kind == TableRowKind.Event)
        {
            ApplyNavSelection(view.LocatorAt(row), shift);
            _focusActiveOnNextRender = true;

            return;
        }

        if (!shift)
        {
            SetCursorHeader(view.GroupAt(row).Key);
            _focusActiveOnNextRender = true;

            return;
        }

        int probe = targetRow;

        while (probe >= 0 && probe < view.Count && view[probe].Kind == TableRowKind.Header)
        {
            probe += scanDirection;
        }

        if (probe < 0 || probe >= view.Count) { return; }

        ApplyNavSelection(view.LocatorAt(view[probe]), shift: true);
        _focusActiveOnNextRender = true;
    }

    private TableCursor? NearestHeaderCursor(int priorVisibleRow)
    {
        var groups = _rowView!.Groups;

        if (groups.Count == 0) { return null; }

        foreach (var group in groups)
        {
            if (group.VisibleStart >= priorVisibleRow) { return TableCursor.ForHeader(group.Key); }
        }

        return TableCursor.ForHeader(groups[^1].Key);
    }

    // Retype an event in a collapsed group to its header.
    private TableCursor? NormalizeCursor(TableCursor? cursor)
    {
        if (_rowView is not { } view ||
            cursor is not { Kind: TableRowKind.Event, Handle: { } handle })
        {
            return cursor;
        }

        int index = RowIndexOf(handle);

        if (index < 0) { return cursor; }

        var group = view.GroupForEvent(index);

        if (group.IsCollapsed) { return TableCursor.ForHeader(group.Key); }

        return cursor;
    }

    private void RebuildGroupedRowView(IEventColumnView displayedEvents)
    {
        var state = _logTableState;

        if (state.GroupBy is not { } groupBy)
        {
            EventLocator? formerGroupFirstLocator = null;

            if (_rowView is { } priorView &&
                _cursor is { Kind: TableRowKind.Header, GroupKey: { } headerKey } &&
                priorView.TryGetGroupByKey(headerKey, out var priorGroup) && priorGroup.EventCount > 0)
            {
                formerGroupFirstLocator = priorView.FirstLocatorOf(priorGroup);
            }

            _rowView = null;
            _rowViewSnapshot = default;

            if (formerGroupFirstLocator is not null)
            {
                SetCursorEvent(formerGroupFirstLocator);
            }

            return;
        }

        var snapshot = (displayedEvents, _currentTable?.Id, state.GroupBy, state.IsGroupDescending,
            state.GroupsCollapsedByDefault, (ImmutableHashSet<string>?)state.GroupCollapseOverrides);

        if (_rowView is not null && _rowViewSnapshot.Equals(snapshot)) { return; }

        int priorHeaderRow = _cursor is { Kind: TableRowKind.Header } ? ResolveCursorVisibleRow() : -1;

        _rowViewSnapshot = snapshot;
        _rowView = GroupedRowView.Build(displayedEvents, groupBy, state.IsGroupCollapsed);

        ReconcileGroupedCursor(priorHeaderRow);
    }

    private void RebuildRowMaps()
    {
        var displayedEvents = ResolveActiveDisplayedEvents();
        _activeDisplayedEvents = displayedEvents;

        // Drop Find's group-expansion ownership when the group-key namespace (active table / GroupBy) has changed.
        PruneFindGroupOwnershipOnContextChange();

        var currentTableId = _currentTable?.Id;

        // Highlight results are keyed by locator (stable within a generation across re-sorts). A reload mints a new
        // EventLogId, so clear the cache only when the active table identity changes, bounding growth without discarding
        // still-valid entries on every re-sort.
        if (!Equals(currentTableId, _highlightCacheTableId))
        {
            _highlightCacheTableId = currentTableId;
            _highlightCache.Clear();
        }

        if (!ReferenceEquals(displayedEvents, _lastIndexedDisplayedEvents))
        {
            _lastIndexedDisplayedEvents = displayedEvents;
            _refreshEventViewportOnRender = true;

            // The event set changed (filter/sort/append/reload) so prior Find matches are stale; collapse/regroup keep the same reference and deliberately don't reach here.
            NotifyFindViewChanged();

            if (displayedEvents.Count == 0)
            {
                _selectionAnchor = null;
                _cursor = null;
            }
            else
            {
                // Locators are physical positions within a generation: a re-sort keeps them valid (Rank >= 0); a
                // reload invalidates them (wrong generation, Rank -1), dropping the stale anchor/cursor.
                if (_selectionAnchor is { } anchor && displayedEvents.Rank(anchor) < 0)
                {
                    _selectionAnchor = null;
                }

                if (_cursor is { Kind: TableRowKind.Event, Handle: { } cursorHandle } &&
                    displayedEvents.Rank(cursorHandle) < 0)
                {
                    _cursor = null;
                }

                if (IsSelectionOutOfSortOrder(_selection))
                {
                    _resortSelectionOnNextRender = true;
                }
            }
        }

        // Outside the events-ref guard: collapse toggles keep the same reference.
        RebuildGroupedRowView(displayedEvents);
    }

    private void ReconcileGroupedCursor(int priorHeaderRow)
    {
        if (_rowView is not { } view || _cursor is not { } cursor) { return; }

        if (cursor is { Kind: TableRowKind.Event, Handle: { } handle })
        {
            int index = RowIndexOf(handle);

            if (index >= 0)
            {
                var group = view.GroupForEvent(index);

                if (group.IsCollapsed) { _cursor = TableCursor.ForHeader(group.Key); }
            }
            else
            {
                _cursor = null;
            }

            return;
        }

        if (cursor is { Kind: TableRowKind.Header, GroupKey: { } key } && !view.TryGetGroupByKey(key, out _))
        {
            _cursor = NearestHeaderCursor(priorHeaderRow);
        }
    }

    private void RescrollToSelected() =>
        _ = InvokeAsync(() =>
        {
            _rescrollToSelectedOnRender = true;
            StateHasChanged();
        });

    private IEventColumnView ResolveActiveDisplayedEvents() =>
        _currentTable is null ? s_emptyView : _logTableState.DisplayedEventsForTab(_currentTable);

    private int ResolveCursorVisibleRow()
    {
        if (_cursor is { Kind: TableRowKind.Header, GroupKey: { } key })
        {
            return _rowView?.VisibleRowForHeader(key) ?? -1;
        }

        if (_cursor is { Kind: TableRowKind.Event, Handle: { } handle })
        {
            int index = RowIndexOf(handle);

            if (index >= 0)
            {
                return _rowView?.VisibleRowForEvent(index) ?? index;
            }
        }

        return -1;
    }

    private void ResortSelectionForCurrentTable()
    {
        DispatchSetSelection(_selection, ActiveHandle ?? _focus?.CurrentHandle);
    }

    private int RowIndexOf(EventLocator locator) => _activeDisplayedEvents.Rank(locator);

    private async Task ScrollToSelectedEvent()
    {
        var target = ActiveHandle
            ?? _focus?.CurrentHandle
            ?? (_selection.Count > 0 ? _selection[^1].CurrentHandle : null);

        if (target is not { } handle) { return; }

        if (_activeDisplayedEvents.Count == 0) { return; }

        // Locators are live positions within the current generation; no value-key re-resolve is needed.
        int index = RowIndexOf(handle);

        if (index < 0) { return; }

        int targetRow = _rowView?.VisibleRowForEvent(index) ?? index;

        if (_tableModule is not null) { await _tableModule.InvokeVoidAsync("scrollToRow", targetRow); }
    }

    private void SelectEvent(MouseEventArgs args, DisplayRow row)
    {
        var displayedEvents = _activeDisplayedEvents;

        switch (args)
        {
            case { ShiftKey: true } when displayedEvents.Count > 0:
                if (_selectionAnchor is null)
                {
                    _selectionAnchor = row.Loc;
                    SetCursorEvent(row.Loc);
                    DispatchSetSelection([EntryFor(row)], row.Loc);

                    return;
                }

                SetCursorEvent(row.Loc);
                var range = BuildRange(_selectionAnchor.Value, row.Loc);

                if (args.CtrlKey)
                {
                    var merged = new List<SelectionEntry>(_selection.Count + range.Count);
                    merged.AddRange(_selection);
                    merged.AddRange(range);
                    DispatchSetSelection(merged, row.Loc);
                }
                else
                {
                    DispatchSetSelection(range, row.Loc, alreadyOrdered: true);
                }

                return;

            case { CtrlKey: true }:
                _selectionAnchor = row.Loc;
                SetCursorEvent(row.Loc);

                if (_selectedSet.Contains(row.Loc))
                {
                    var remaining = new List<SelectionEntry>(_selection.Count);

                    foreach (var existing in _selection)
                    {
                        // Toggle off by live position (CurrentHandle), not OriginHandle; they differ after a reload.
                        if (existing.CurrentHandle != row.Loc) { remaining.Add(existing); }
                    }

                    DispatchSetSelection(remaining, row.Loc);
                }
                else
                {
                    var combined = new List<SelectionEntry>(_selection.Count + 1);
                    combined.AddRange(_selection);
                    combined.Add(EntryFor(row));
                    DispatchSetSelection(combined, row.Loc);
                }

                return;

            default:
                if (args.Button == 2 && _selectedSet.Contains(row.Loc))
                {
                    SetCursorEvent(row.Loc);
                    DispatchSetSelection(_selection, row.Loc);

                    return;
                }

                _selectionAnchor = row.Loc;
                SetCursorEvent(row.Loc);
                DispatchSetSelection([EntryFor(row)], row.Loc);

                return;
        }
    }

    private void SelectGroup(EventGroup group)
    {
        if (group.EventCount == 0 ||
            group.StartIndex + group.EventCount > _activeDisplayedEvents.Count)
        {
            return;
        }

        var members = _activeDisplayedEvents.Slice(group.StartIndex, group.EventCount);
        var entries = new List<SelectionEntry>(members.Count);

        foreach (var row in members) { entries.Add(EntryFor(row)); }

        var activeLocator = members[0].Loc;
        _selectionAnchor = activeLocator;
        SetCursorEvent(activeLocator);
        DispatchSetSelection(entries, activeLocator, alreadyOrdered: true);
    }

    private void SelectGroupByKey(string key)
    {
        if (_rowView is null || !_rowView.TryGetGroupByKey(key, out var group)) { return; }

        SelectGroup(group);
    }

    private void SetCursor(TableCursor? cursor) => _cursor = NormalizeCursor(cursor);

    private void SetCursorEvent(EventLocator? handle) =>
        SetCursor(handle is { } locator ? TableCursor.ForEvent(locator) : null);

    private void SetCursorHeader(string groupKey) => SetCursor(TableCursor.ForHeader(groupKey));

    private void SetGroupCollapsed(string key, bool collapse)
    {
        if (_rowView is null || !_rowView.TryGetGroupByKey(key, out _)) { return; }

        if (LogTableState.Value.IsGroupCollapsed(key) != collapse)
        {
            LogTableCommands.ToggleGroupCollapsed(key);
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

        var groupItems = new List<MenuItem>
        {
            MenuItem.Item(
                "(none)",
                () => { if (state.GroupBy is not null) { LogTableCommands.SetGroupBy(null); } },
                isChecked: state.GroupBy is null)
        };

        foreach (var (column, _) in state.Columns)
        {
            var capturedColumn = column;
            groupItems.Add(MenuItem.Item(
                column.ToFullString(),
                () => { if (!state.GroupBy.Equals(capturedColumn)) { LogTableCommands.SetGroupBy(capturedColumn); } },
                isChecked: state.GroupBy.Equals(capturedColumn)));
        }

        items.Add(MenuItem.SubMenu("Group By", groupItems));

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
            MenuItem.Item(
                "Show Related by Activity ID",
                () => FilterLensCommands.ShowRelatedByActivityId(selectedEvent.ActivityId, selectedEvent.OwningLog),
                isEnabled: selectedEvent.ActivityId.HasValue,
                disabledReason: selectedEvent.ActivityId.HasValue ? null : "This event has no Activity ID."),
            MenuItem.Item(
                "Show Events Sharing Related Activity ID",
                () => FilterLensCommands.ShowRelatedByRelatedActivityId(selectedEvent.RelatedActivityId, selectedEvent.OwningLog),
                isEnabled: selectedEvent.RelatedActivityId.HasValue,
                disabledReason: selectedEvent.RelatedActivityId.HasValue ? null : "This event has no Related Activity ID."),
            MenuItem.Item(
                "Show Parent Activity",
                () => FilterLensCommands.ShowParentActivity(selectedEvent.RelatedActivityId, selectedEvent.OwningLog),
                isEnabled: selectedEvent.RelatedActivityId.HasValue,
                disabledReason: selectedEvent.RelatedActivityId.HasValue ? null : "This event has no Related Activity ID."),
            MenuItem.SubMenu(
                "Show Events Near This Time",
                [
                    MenuItem.Item(
                        "\u00b130 Seconds",
                        () => FilterLensCommands.ShowEventsNearTime(
                            selectedEvent.TimeCreated, TimeSpan.FromSeconds(30), Settings.TimeZoneInfo, selectedEvent.OwningLog)),
                    MenuItem.Item(
                        "\u00b11 Minute",
                        () => FilterLensCommands.ShowEventsNearTime(
                            selectedEvent.TimeCreated, TimeSpan.FromMinutes(1), Settings.TimeZoneInfo, selectedEvent.OwningLog)),
                    MenuItem.Item(
                        "\u00b15 Minutes",
                        () => FilterLensCommands.ShowEventsNearTime(
                            selectedEvent.TimeCreated, TimeSpan.FromMinutes(5), Settings.TimeZoneInfo, selectedEvent.OwningLog)),
                    MenuItem.Item(
                        "\u00b115 Minutes",
                        () => FilterLensCommands.ShowEventsNearTime(
                            selectedEvent.TimeCreated, TimeSpan.FromMinutes(15), Settings.TimeZoneInfo, selectedEvent.OwningLog)),
                    MenuItem.Item(
                        "\u00b11 Hour",
                        () => FilterLensCommands.ShowEventsNearTime(
                            selectedEvent.TimeCreated, TimeSpan.FromHours(1), Settings.TimeZoneInfo, selectedEvent.OwningLog)),
                ]),
            MenuItem.Separator(),
            MenuItem.SubMenu(
                "More Fields",
                [
                    MenuItem.SubMenu("Include", ShowEventFieldItems(selectedEvent, false)),
                    MenuItem.SubMenu("Exclude", ShowEventFieldItems(selectedEvent, true)),
                ]),
        ];
    }

    private IReadOnlyList<MenuItem> ShowEventFieldItems(ResolvedEvent selectedEvent, bool exclude)
    {
        var items = new List<MenuItem>();

        foreach (EventProperty property in Enum.GetValues<EventProperty>())
        {
            if (property is EventProperty.Description or EventProperty.Xml) { continue; }

            var capturedProperty = property;
            bool hasValue = CellFilterBuilder.TryGetDisplayValue(selectedEvent, property, out _);

            items.Add(MenuItem.Item(
                property.ToFullString(),
                () => ApplySelectedFilter(selectedEvent, capturedProperty, exclude),
                isEnabled: hasValue,
                disabledReason: hasValue ? null : NoCellValueReason));
        }

        return items;
    }

    private IReadOnlyList<MenuItem> ShowGroupContextMenuItems(EventGroup group)
    {
        bool collapsedNow = LogTableState.Value.IsGroupCollapsed(group.Key);

        return
        [
            MenuItem.Item(
                collapsedNow ? "Expand Group" : "Collapse Group",
                () => UserSetGroupCollapsed(group.Key, !collapsedNow)),
            MenuItem.Item("Expand All Groups", () => LogTableCommands.SetAllGroupsCollapsed(false)),
            MenuItem.Item("Collapse All Groups", () => LogTableCommands.SetAllGroupsCollapsed(true)),
            MenuItem.Separator(),
            MenuItem.Item(
                "Group Descending",
                () => LogTableCommands.ToggleGroupSortDirection(),
                isChecked: LogTableState.Value.IsGroupDescending),
            MenuItem.Separator(),
            MenuItem.Item("Select Group", () => SelectGroupByKey(group.Key)),
        ];
    }

    private void ToggleGroupCollapsed(string groupKey)
    {
        // A manual toggle of a Find-expanded group hands ownership back to the user, so Find will not later re-collapse it.
        _findExpandedGroupKeys.Remove(groupKey);
        LogTableCommands.ToggleGroupCollapsed(groupKey);
    }

    private void ToggleSorting() => LogTableCommands.ToggleSortDirection();

    private async Task<int> TryRefreshPageSize()
    {
        try
        {
            int measured = _tableModule is not null ? await _tableModule.InvokeAsync<int>("getEventTablePageSize") : 0;

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
