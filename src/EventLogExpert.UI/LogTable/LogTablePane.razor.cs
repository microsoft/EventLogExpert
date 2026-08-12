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
using EventLogExpert.Runtime.Settings;
using EventLogExpert.UI.Common;
using EventLogExpert.UI.Common.Interop;
using EventLogExpert.UI.LogTable.Grouping;
using EventLogExpert.UI.Menu;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using Microsoft.JSInterop;
using System.Collections.Immutable;

namespace EventLogExpert.UI.LogTable;

public sealed partial class LogTablePane
{
    private const int DefaultPageSize = 20;
    private const float EventRowHeightPixels = 22f;
    private const int MenuValueMaxLength = 40;
    private const string NoCellValueReason = "No value in this cell to filter on";

    private static readonly IEventColumnView s_emptyView = LogTableState.EmptyView;
    private static readonly HashSet<int> s_warnedUnknownColors = [];

    private readonly Dictionary<EventLocator, string?> _highlightCache = [];

    private IEventColumnView _activeDisplayedEvents = s_emptyView;
    private SavedFilter[] _activeHighlightFilters = [];
    private bool _busyAssertedOnLastPaint;
    private bool _busyHeldForRefresh;
    private TableCursor? _cursor;
    private bool _disposed;
    private DotNetObjectReference<LogTablePane>? _dotNetRef;
    private ColumnName[] _enabledColumns = null!;
    private Virtualize<DisplayRow>? _eventVirtualize;
    private ImmutableList<SavedFilter> _filters = [];
    private int _filtersHighlightKey;
    private SelectionEntry? _focus;
    private bool _focusActiveOnNextRender;
    private string _headerName = string.Empty;
    private EventLogId? _highlightCacheTableId;
    private DisplayedIndicator _indicator = DisplayedIndicator.Nothing;
    private bool _indicatorRenderRequested;
    private DisplayIndicatorState _indicatorState = null!;
    private IEventColumnView? _lastIndexedDisplayedEvents;
    private int _pageSize = DefaultPageSize;
    private ColumnName[] _previousEnabledColumns = [];
    private bool _refreshEventViewportOnRender;
    private long _renderedPresentationRevision = -1;
    private bool _rescrollToSelectedOnRender;
    private IEventColumnView? _rescrolledForView;
    private bool _resortSelectionOnNextRender;
    private GroupedRowView? _rowView;
    private (IEventColumnView View, EventLogId? TableId, ColumnName? GroupBy, bool GroupDescending, bool CollapsedDefault, ImmutableHashSet<string>? Overrides) _rowViewSnapshot;
    private HashSet<EventLocator> _selectedSet = [];
    private ImmutableList<SelectionEntry> _selection = [];
    private EventLocator? _selectionAnchor;
    private IJSObjectReference? _tableModule;
    private TimeZoneInfo _timeZoneSettings = null!;
    private bool _viewportRenderRequested;

    private EventLocator? ActiveHandle =>
        _cursor is { Kind: TableRowKind.Event, Handle: { } handle } ? handle : null;

    [Inject] private IClipboardService ClipboardService { get; init; } = null!;

    [Inject] private ILogTableColumnDefaultsProvider ColumnDefaults { get; init; } = null!;

    [Inject] private IEventLogCommands EventLogCommands { get; init; } = null!;

    [Inject] private IFilterLensCommands FilterLensCommands { get; init; } = null!;

    [Inject] private IFilterPaneCommands FilterPaneCommands { get; init; } = null!;

    [Inject] private IActiveFiltersSource FilterSelection { get; init; } = null!;

    [Inject] private IFilterService FilterService { get; init; } = null!;

    [Inject] private IEventFocusSource Focus { get; init; } = null!;

    [Inject] private IGroupCollapseNotifier GroupCollapseNotifier { get; init; } = null!;

    [Inject] private IHighlightSelector HighlightSelector { get; init; } = null!;

    [Inject] private DisplayIndicatorGate IndicatorGate { get; init; } = null!;

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    [Inject] private ILogTableCommands LogTableCommands { get; init; } = null!;

    [Inject] private IMenuService MenuService { get; init; } = null!;

    [Inject] private IRevealFocusSource RevealFocusSource { get; init; } = null!;

    [Inject] private IEventSelectionSource Selection { get; init; } = null!;

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

    internal static string GetLevelClass(string level) => SeverityIcon.CssClass(LevelSeverity.FromLevelName(level));

    protected override async ValueTask DisposeAsyncCore(bool disposing)
    {
        if (disposing)
        {
            _disposed = true;

            Settings.TimeZoneChanged -= OnTimeZoneChanged;

            DisposeFind();

            _indicatorState?.Dispose();

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
        bool rescrollRequested = _rescrollToSelectedOnRender;
        _rescrollToSelectedOnRender = false;

        _busyAssertedOnLastPaint = IsGridBusy();

        _indicatorState.RecordPaint(_indicator);

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
                }
                catch (JSDisconnectedException) { /* Circuit gone; nothing to refresh. */ }
                catch (Exception e)
                {
                    TraceLogger.Error($"Failed to refresh the event viewport: {e}");
                }
                finally
                {
                    _busyHeldForRefresh = false;
                    _viewportRenderRequested = true;

                    StateHasChanged();
                }
            }
            else
            {
                ReleaseBusyHeldForRefresh();
            }
        }

        if (RevealFocusSource.Current is { } revealTarget)
        {
            var liveSelection = Selection.Current;
            var currentFocusTarget = Focus.Current?.CurrentHandle
                ?? (liveSelection.Count > 0 ? liveSelection[^1].CurrentHandle : null);

            if (currentFocusTarget != revealTarget)
            {
                EventLogCommands.ConsumeRevealFocus(revealTarget);
            }
            else
            {
                try
                {
                    if (await TryScrollToRow(revealTarget))
                    {
                        EventLogCommands.ConsumeRevealFocus(revealTarget);
                        rescrollRequested = false;
                    }
                }
                catch (JSDisconnectedException) { /* Circuit gone; leave the reveal pending so a reconnected or remounted pane can retry. */ }
                catch (Exception e)
                {
                    EventLogCommands.ConsumeRevealFocus(revealTarget);
                    TraceLogger.Error($"Failed to scroll to the restored selection: {e}");
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

        // the queue has drained - turns that silent staleness into one extra pass. Cannot spin: if the pass still
        // cannot adopt, ShouldRender returns false, no render happens, and this method does not run again.
        if (Presentation.Revision != _renderedPresentationRevision) { StateHasChanged(); }

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override async Task OnInitializedAsync()
    {
        ObserveSource(Focus);
        ObserveSource(Selection);
        ObserveSource(FilterSelection);

        ObserveSource(
            handler => GroupCollapseNotifier.Requested += handler,
            handler => GroupCollapseNotifier.Requested -= handler,
            OnGroupCollapseRequestedAsync);

        ObserveSource(RevealFocusSource);

        // The time zone is a plain service event, not Fluxor, and every date cell is rendered through it. FluxorComponent
        // used to repaint this surface often enough to pick a change up for free; now it must be subscribed directly.
        Settings.TimeZoneChanged += OnTimeZoneChanged;

        // Owned per component rather than injected: the minimum-visible clock it holds is measured from THIS pane's
        // paints, and the render its floor asks for has to be marshalled onto this pane's dispatcher.
        _indicatorState = new DisplayIndicatorState(IndicatorGate, RequestIndicatorRender);

        _enabledColumns = GetOrderedEnabledColumns();
        _focus = Focus.Current;
        SetCursorEvent(_focus?.CurrentHandle);
        _selection = Selection.Current;
        _selectedSet = BuildSelectedSet(_selection);
        _filters = FilterSelection.Current;
        _activeHighlightFilters = HighlightSelector.Select(_filters);
        _filtersHighlightKey = HighlightSelector.ComputeHighlightKey(_filters);
        _timeZoneSettings = Settings.TimeZoneInfo;

        // Seed the rescroll guard with the view the pane initialises on. OnPresentationChanged rescrolls only when the
        // view instance differs from this; the base sets Presentation without ever raising that callback, so leaving the
        // guard null would make the FIRST publication over these same rows (a collapse, a width change, a fault or
        // staleness transition - all now advance the revision without moving a row) read as a row change and yank the
        // viewport to the selection.
        _rescrolledForView = Presentation.View;

        WarnOnUnknownFilterColors(_filters);

        RebuildRowMaps();

        RegisterFind();

        await base.OnInitializedAsync();
    }

    protected override void OnPresentationChanged()
    {
        if (ReferenceEquals(Presentation.View, _rescrolledForView)) { return; }

        _rescrolledForView = Presentation.View;
        _rescrollToSelectedOnRender = true;
    }

    protected override bool ShouldRender()
    {
        var currentFilters = FilterSelection.Current;
        bool filtersChanged = !ReferenceEquals(currentFilters, _filters);
        bool focusChanged = Focus.Current?.OriginHandle != _focus?.OriginHandle;

        if (!_focusActiveOnNextRender &&
            !_viewportRenderRequested &&
            !_rescrollToSelectedOnRender &&
            RevealFocusSource.Current is null &&
            !_findRenderRequested &&
            !_indicatorRenderRequested &&
            Presentation.Revision == _renderedPresentationRevision &&
            ReferenceEquals(Selection.Current, _selection) &&
            !focusChanged &&
            !filtersChanged &&
            Settings.TimeZoneInfo.Equals(_timeZoneSettings)) { return false; }

        _viewportRenderRequested = false;
        _findRenderRequested = false;
        _indicatorRenderRequested = false;
        _renderedPresentationRevision = Presentation.Revision;

        _indicator = _indicatorState.Resolve(
            Presentation.IndicatorKind,
            Presentation.Revision,

            _busyHeldForRefresh);

        bool selectionChanged = !ReferenceEquals(Selection.Current, _selection);

        var previousColumnsForFind = _enabledColumns;
        _enabledColumns = GetOrderedEnabledColumns();
        bool findSearchTextChanged = _findOpen && !previousColumnsForFind.SequenceEqual(_enabledColumns);

        if (selectionChanged)
        {
            _selection = Selection.Current;
            _selectedSet = BuildSelectedSet(_selection);
        }

        if (focusChanged)
        {
            _focus = Focus.Current;
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
        var set = new HashSet<EventLocator>(selection.Count);

        foreach (var entry in selection)
        {
            if (entry.CurrentHandle is { } handle) { set.Add(handle); }
        }

        return set;
    }

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
        Presentation.ColumnWidths.TryGetValue(column, out int width) ? width : ColumnDefaults.GetColumnWidth(column);

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

    private string GetGroupName() => Presentation.Ordering.GroupBy?.ToFullString() ?? string.Empty;

    private string GetGroupValueText(EventGroup group)
    {
        if (group.EventCount == 0) { return "(none)"; }

        var representative = _activeDisplayedEvents.GetDetailLean(_activeDisplayedEvents.LocatorAt(group.StartIndex));

        string? value = Presentation.Ordering.GroupBy switch
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
            ColumnName.User => representative.UserDisplayName,
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
        if (!_activeDisplayedEvents.TryGetDetail(row.Loc, out var detail)) { return null; }

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
        [.. LogTableState.ResolveOrderedEnabledColumns(Presentation.Columns, Presentation.ColumnOrder, ColumnDefaults)];

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

    private string? IndicatorSentence() =>
        _indicator.Sentence switch
        {
            DisplayIndicatorKind.EmptyPending => "Loading events\u2026",
            DisplayIndicatorKind.ReorderPending => "Reordering events\u2026",
            DisplayIndicatorKind.Fault => Presentation.FaultCause is { Length: > 0 } cause ?
                $"These events could not be prepared. {cause}" :
                "These events could not be prepared.",
            _ => null
        };

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
        if (!_activeDisplayedEvents.TryGetDetail(row.Loc, out var detail)) { return; }

        var items = new List<MenuItem>();

        AppendCellFilterItems(items, detail, column);
        items.AddRange(ShowContextMenuItems(detail));

        MenuService.OpenAt(args.ClientX, args.ClientY, items);
    }

    private void InvokeContextMenu(MouseEventArgs args)
    {
        if (Focus.Current?.CurrentHandle is not { } handle) { return; }

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

    private bool IsGridBusy() =>
        Presentation.IndicatorKind == DisplayIndicatorKind.EmptyPending || _busyHeldForRefresh;

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

    private void OnTimeZoneChanged(object? sender, TimeZoneInfo value) => RequestAppStateRender();

    private void RebuildGroupedRowView(IEventColumnView displayedEvents, bool absenceIsFinal)
    {
        if (Presentation.Ordering.GroupBy is not { } groupBy)
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

        var snapshot = (displayedEvents,
            Presentation.ActiveTabId,
            Presentation.Ordering.GroupBy,
            Presentation.Ordering.IsGroupDescending,
            Presentation.GroupsCollapsedByDefault,
            (ImmutableHashSet<string>?)Presentation.GroupCollapseOverrides);

        if (_rowView is not null && _rowViewSnapshot.Equals(snapshot)) { return; }

        int priorHeaderRow = _cursor is { Kind: TableRowKind.Header } ? ResolveCursorVisibleRow() : -1;

        _rowViewSnapshot = snapshot;
        _rowView = GroupedRowView.Build(displayedEvents, groupBy, Presentation.IsGroupCollapsed);

        ReconcileGroupedCursor(priorHeaderRow, absenceIsFinal);
    }

    private void RebuildRowMaps()
    {
        var displayedEvents = ResolveActiveDisplayedEvents();
        _activeDisplayedEvents = displayedEvents;

        PruneFindGroupOwnershipOnContextChange();

        var currentTableId = Presentation.ActiveTabId;

        bool absenceIsFinal = displayedEvents.Count > 0 ||
            (currentTableId is not null && Presentation.State == PresentationState.Current);

        if (!Equals(currentTableId, _highlightCacheTableId))
        {
            _highlightCacheTableId = currentTableId;
            _highlightCache.Clear();
        }

        if (!ReferenceEquals(displayedEvents, _lastIndexedDisplayedEvents))
        {
            _lastIndexedDisplayedEvents = displayedEvents;
            _refreshEventViewportOnRender = true;

            if (_busyAssertedOnLastPaint) { _busyHeldForRefresh = true; }

            NotifyFindViewChanged();

            if (displayedEvents.Count > 0)
            {
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

        if (absenceIsFinal && displayedEvents.Count == 0)
        {
            _selectionAnchor = null;
            _cursor = null;
        }

        RebuildGroupedRowView(displayedEvents, absenceIsFinal);
    }

    private void ReconcileGroupedCursor(int priorHeaderRow, bool absenceIsFinal)
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
            else if (absenceIsFinal)
            {
                _cursor = null;
            }

            return;
        }

        if (absenceIsFinal &&
            cursor is { Kind: TableRowKind.Header, GroupKey: { } key } &&
            !view.TryGetGroupByKey(key, out _))
        {
            _cursor = NearestHeaderCursor(priorHeaderRow);
        }
    }

    private void ReleaseBusyHeldForRefresh()    {
        if (!_busyHeldForRefresh) { return; }

        _busyHeldForRefresh = false;
        _viewportRenderRequested = true;

        StateHasChanged();
    }

    private void RequestAppStateRender() => RequestGuardedRender(StateHasChanged);

    private void RequestIndicatorRender() =>
        RequestGuardedRender(() =>
        {
            _indicatorRenderRequested = true;

            StateHasChanged();
        });

    private IEventColumnView ResolveActiveDisplayedEvents() =>
        Presentation.ActiveTabId is not null ?
            Presentation.View :
            s_emptyView;

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

        if (Presentation.IsGroupCollapsed(key) != collapse)
        {
            LogTableCommands.ToggleGroupCollapsed(key);
        }
    }

    private IReadOnlyList<MenuItem> ShowColumnMenuItems()
    {
        var columns = Presentation.Columns;
        var ordering = Presentation.Ordering;
        var items = new List<MenuItem>();

        foreach (var (column, isVisible) in columns)
        {
            var capturedColumn = column;
            items.Add(MenuItem.Item(
                column.ToFullString(),
                () => LogTableCommands.ToggleColumn(capturedColumn),
                isChecked: isVisible));
        }

        items.Add(MenuItem.Separator());

        var orderItems = new List<MenuItem>();
        foreach (var (column, _) in columns)
        {
            var capturedColumn = column;
            orderItems.Add(MenuItem.Item(
                column.ToFullString(),
                () => LogTableCommands.SetOrderBy(capturedColumn),
                isChecked: ordering.OrderBy.Equals(capturedColumn)));
        }

        items.Add(MenuItem.SubMenu("Order By", orderItems));

        var groupItems = new List<MenuItem>
        {
            MenuItem.Item(
                "(none)",
                () => { if (ordering.GroupBy is not null) { LogTableCommands.SetGroupBy(null); } },
                isChecked: ordering.GroupBy is null)
        };

        foreach (var (column, _) in columns)
        {
            var capturedColumn = column;
            groupItems.Add(MenuItem.Item(
                column.ToFullString(),
                () => { if (!ordering.GroupBy.Equals(capturedColumn)) { LogTableCommands.SetGroupBy(capturedColumn); } },
                isChecked: ordering.GroupBy.Equals(capturedColumn)));
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
            if (property is EventProperty.Description or EventProperty.Xml or EventProperty.UserId) { continue; }

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
        bool collapsedNow = Presentation.IsGroupCollapsed(group.Key);

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
                isChecked: Presentation.Ordering.IsGroupDescending),
            MenuItem.Separator(),
            MenuItem.Item("Select Group", () => SelectGroupByKey(group.Key)),
        ];
    }

    private void ToggleGroupCollapsed(string groupKey)
    {
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

    private async Task<bool> TryScrollToRow(EventLocator target)
    {
        if (_activeDisplayedEvents.Count == 0) { return false; }

        int index = RowIndexOf(target);

        if (index < 0) { return false; }

        if (_tableModule is null) { return false; }

        int targetRow = _rowView?.VisibleRowForEvent(index) ?? index;
        await _tableModule.InvokeVoidAsync("scrollToRow", targetRow);

        return true;
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
