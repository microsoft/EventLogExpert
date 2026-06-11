// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Basic;
using EventLogExpert.Filtering.Common.Filtering;
using EventLogExpert.Filtering.Compilation;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.Common.Display;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.UI.Common.Interop;
using EventLogExpert.UI.LogTable.Grouping;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Collections.Immutable;
using FilterMode = EventLogExpert.Filtering.Evaluation.FilterMode;

namespace EventLogExpert.UI.LogTable;

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
    private TableCursor? _cursor;
    private DotNetObjectReference<LogTablePane>? _dotNetRef;
    private ColumnName[] _enabledColumns = null!;
    private ImmutableList<SavedFilter> _filters = [];
    private int _filtersHighlightKey;
    private bool _focusActiveOnNextRender;
    private string _headerName = string.Empty;
    private IReadOnlyList<ResolvedEvent>? _lastIndexedDisplayedEvents;
    private LogTableState _logTableState = null!;
    private int _pageSize = DefaultPageSize;
    private ColumnName[] _previousEnabledColumns = [];
    private bool _resortSelectionOnNextRender;
    private Dictionary<ResolvedEvent, int> _rowIndexMap = new(ReferenceEqualityComparer.Instance);
    private GroupedRowView? _rowView;
    private (IReadOnlyList<ResolvedEvent> Events, EventLogId? TableId, ColumnName? GroupBy, bool GroupDescending, bool CollapsedDefault, ImmutableHashSet<string>? Overrides) _rowViewSnapshot;
    private ResolvedEvent? _selectedEvent;
    private ImmutableList<ResolvedEvent> _selectedEvents = [];
    private HashSet<ResolvedEvent> _selectedSet = new(ReferenceEqualityComparer.Instance);
    private ResolvedEvent? _selectionAnchor;
    private IJSObjectReference? _tableModule;
    private TimeZoneInfo _timeZoneSettings = null!;

    private ResolvedEvent? ActiveEvent =>
        _cursor is { Kind: TableRowKind.Event, Event: { } @event } ? @event : null;

    [Inject] private IClipboardService ClipboardService { get; init; } = null!;

    [Inject] private ILogTableColumnDefaultsProvider ColumnDefaults { get; init; } = null!;

    [Inject] private IEventLogCommands EventLogCommands { get; init; } = null!;

    [Inject] private IFilterPaneCommands FilterPaneCommands { get; init; } = null!;

    [Inject] private IState<FilterPaneState> FilterPaneState { get; init; } = null!;

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
            catch (JSDisconnectedException) { /* Circuit gone - fall back to default page size. */ }
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
        SetCursorEvent(_selectedEvent);
        _selectedEvents = SelectedEvents.Value;
        _selectedSet = new HashSet<ResolvedEvent>(_selectedEvents, ReferenceEqualityComparer.Instance);
        var initialPaneState = FilterPaneState.Value;
        _filters = initialPaneState.Filters;
        _activeHighlightFilters = HighlightSelector.Select(initialPaneState.Filters);
        _filtersHighlightKey = HighlightSelector.ComputeHighlightKey(initialPaneState.Filters);
        _timeZoneSettings = Settings.TimeZoneInfo;

        WarnOnUnknownFilterColors(_filters);

        RebuildRowMaps();

        await base.OnInitializedAsync();
    }

    protected override bool ShouldRender()
    {
        var currentPaneState = FilterPaneState.Value;
        var currentFilters = currentPaneState.Filters;
        bool filtersChanged = !ReferenceEquals(currentFilters, _filters);
        bool selectedEventChanged = !ReferenceEquals(SelectedEvent.Value, _selectedEvent);

        // Also render on a pending focus move, which changes no Fluxor state.
        if (!_focusActiveOnNextRender &&
            ReferenceEquals(LogTableState.Value, _logTableState) &&
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
            // Reference equality, not value: value-equality would hash strings per change.
            _selectedSet = new HashSet<ResolvedEvent>(_selectedEvents, ReferenceEqualityComparer.Instance);
        }

        if (selectedEventChanged)
        {
            _selectedEvent = SelectedEvent.Value;
            SetCursorEvent(_selectedEvent);
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

        RebuildRowMaps();

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

            // Skip null RecordId: null == null would merge distinct error-read events.
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

    private void ApplyNavSelection(IReadOnlyList<ResolvedEvent> displayedEvents, ResolvedEvent targetEvent, bool shift)
    {
        if (shift)
        {
            _selectionAnchor ??= ActiveEvent ?? targetEvent;
            SetCursorEvent(targetEvent);
            DispatchSetSelection(BuildRange(displayedEvents, _selectionAnchor, targetEvent), targetEvent);
        }
        else
        {
            _selectionAnchor = targetEvent;
            SetCursorEvent(targetEvent);
            DispatchSetSelection([targetEvent], targetEvent);
        }
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

        if (!BasicFilterFormatter.TryFormat(basicFilter, out var comparisonString)) { return; }

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
        int visibleRow = ResolveCursorVisibleRow();

        // A negative index would target aria-rowindex=1 (the header row).
        if (visibleRow < 0) { return; }

        try
        {
            if (_tableModule is not null) { await _tableModule.InvokeVoidAsync("focusEventTableRow", visibleRow); }
        }
        catch (JSDisconnectedException) { /* Circuit gone - focus best-effort during teardown. */ }
        catch (Exception e)
        {
            TraceLogger.Warning($"Failed to focus active table row: {e}");
        }
    }

    private int GetAriaRowCount() => (_rowView?.Count ?? _activeDisplayedEvents.Count) + 1;

    private int GetColumnWidth(ColumnName column) =>
        _logTableState.ColumnWidths.TryGetValue(column, out int width) ? width : ColumnDefaults.GetColumnWidth(column);

    private string GetCss(ResolvedEvent @event) =>
        _selectedSet.Contains(@event) ? "table-row selected" : "table-row";

    private int GetCurrentVisibleRow(IReadOnlyList<ResolvedEvent> displayedEvents)
    {
        int cursorRow = ResolveCursorVisibleRow();

        if (cursorRow >= 0) { return cursorRow; }

        var fallback = _selectedEvents.LastOrDefault();

        if (fallback is not null && _rowIndexMap.TryGetValue(fallback, out int fallbackIndex))
        {
            return _rowView?.VisibleRowForEvent(fallbackIndex) ?? fallbackIndex;
        }

        int count = _rowView?.Count ?? displayedEvents.Count;

        return count > 0 ? 0 : -1;
    }

    private string GetDateColumnHeader() =>
        Settings.TimeZoneInfo.Equals(TimeZoneInfo.Local) ?
            "Date and Time" :
            $"Date and Time {Settings.TimeZoneInfo.DisplayName.Split(" ").First()}";

    private string GetGroupName() => _logTableState.GroupBy?.ToFullString() ?? string.Empty;

    private string GetGroupValueText(EventGroup group)
    {
        if (group.EventCount == 0) { return "(none)"; }

        var representative = _activeDisplayedEvents[group.StartIndex];

        string? value = _logTableState.GroupBy switch
        {
            ColumnName.Level => representative.Level,
            ColumnName.DateAndTime => representative.TimeCreated.ConvertTimeZone(_timeZoneSettings).ToString(),
            ColumnName.ActivityId => representative.ActivityId?.ToString(),
            ColumnName.Log => GetLogShortName(representative.OwningLog),
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

    private string? GetHighlight(ResolvedEvent @event)
    {
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
            return ColumnDefaults.ColumnOrder.Where(enabledSet.Contains).ToArray();
        }

        return _logTableState.ColumnOrder
            .Where(enabledSet.Contains)
            .ToArray();
    }

    private int GetRowIndex(ResolvedEvent evt)
    {
        if (!_rowIndexMap.TryGetValue(evt, out int index)) { return 2; }

        return (_rowView?.VisibleRowForEvent(index) ?? index) + 2;
    }

    private int GetRowStripe(ResolvedEvent evt)
    {
        if (!_rowIndexMap.TryGetValue(evt, out int index)) { return 0; }

        if (_rowView is null) { return index % 2; }

        return (index - _rowView.GroupForEvent(index).StartIndex) % 2;
    }

    private async Task HandleKeyDown(KeyboardEventArgs args)
    {
        var displayedEvents = _activeDisplayedEvents;

        if (displayedEvents.Count == 0) { return; }

        if (args is { CtrlKey: true, Code: "KeyC" })
        {
            await ClipboardService.CopySelectedEvent();

            return;
        }

        if (args is { CtrlKey: true, Code: "KeyA" })
        {
            var last = displayedEvents[^1];
            _selectionAnchor = displayedEvents[0];
            SetCursorEvent(last);
            DispatchSetSelection(displayedEvents, last);

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
            ApplyNavSelection(displayedEvents, displayedEvents[targetRow], args.ShiftKey);
            _focusActiveOnNextRender = true;

            return;
        }

        NavigateGroupedTo(displayedEvents, targetRow, scanDirection, args.ShiftKey);
    }

    private void HandleTreegridLeft()
    {
        var view = _rowView;

        if (view is null) { return; }

        if (_cursor is { Kind: TableRowKind.Event, Event: { } @event } &&
            _rowIndexMap.TryGetValue(@event, out int index))
        {
            SetCursorHeader(view.GroupForEvent(index).Key);
            _focusActiveOnNextRender = true;

            return;
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
            var first = _activeDisplayedEvents[group.StartIndex];
            _selectionAnchor = first;
            SetCursorEvent(first);
            DispatchSetSelection([first], first);
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

    private void InvokeContextMenu(MouseEventArgs args)
    {
        var clicked = SelectedEvent.Value;

        if (clicked is null) { return; }

        MenuService.OpenAt(args.ClientX, args.ClientY, ShowContextMenuItems(clicked));
    }

    private void InvokeGroupContextMenu(MouseEventArgs args, EventGroup group)
    {
        SetCursorHeader(group.Key);
        MenuService.OpenAt(args.ClientX, args.ClientY, ShowGroupContextMenuItems(group));
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

    private void NavigateGroupedTo(
        IReadOnlyList<ResolvedEvent> displayedEvents,
        int targetRow,
        int scanDirection,
        bool shift)
    {
        var view = _rowView!;
        var row = view[targetRow];

        if (row.Kind == TableRowKind.Event)
        {
            ApplyNavSelection(displayedEvents, view.EventAt(row), shift);
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

        ApplyNavSelection(displayedEvents, view.EventAt(view[probe]), shift: true);
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
        if (_rowView is { } view &&
            cursor is { Kind: TableRowKind.Event, Event: { } @event } &&
            _rowIndexMap.TryGetValue(@event, out int index))
        {
            var group = view.GroupForEvent(index);

            if (group.IsCollapsed) { return TableCursor.ForHeader(group.Key); }
        }

        return cursor;
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

    private void RebuildGroupedRowView(IReadOnlyList<ResolvedEvent> displayedEvents)
    {
        var state = _logTableState;

        if (state.GroupBy is not { } groupBy)
        {
            int firstEventIndex = -1;

            if (_rowView is { } priorView &&
                _cursor is { Kind: TableRowKind.Header, GroupKey: { } headerKey } &&
                priorView.TryGetGroupByKey(headerKey, out var priorGroup) && priorGroup.EventCount > 0)
            {
                firstEventIndex = priorGroup.StartIndex;
            }

            _rowView = null;
            _rowViewSnapshot = default;

            if (firstEventIndex >= 0 && firstEventIndex < displayedEvents.Count)
            {
                SetCursorEvent(displayedEvents[firstEventIndex]);
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

        if (!ReferenceEquals(displayedEvents, _lastIndexedDisplayedEvents))
        {
            _lastIndexedDisplayedEvents = displayedEvents;
            _rowIndexMap = new Dictionary<ResolvedEvent, int>(displayedEvents.Count, ReferenceEqualityComparer.Instance);
            _highlightCache.Clear();

            if (displayedEvents.Count == 0)
            {
                _selectionAnchor = null;
                _cursor = null;
            }
            else
            {
                for (int i = 0; i < displayedEvents.Count; i++)
                {
                    _rowIndexMap[displayedEvents[i]] = i;
                }

                _selectionAnchor = ResolveByKey(displayedEvents, _selectionAnchor);

                if (_cursor is { Kind: TableRowKind.Event, Event: { } cursorEvent })
                {
                    var resolved = ResolveByKey(displayedEvents, cursorEvent);
                    _cursor = resolved is null ? null : TableCursor.ForEvent(resolved);
                }

                if (IsSelectionOutOfSortOrder(_selectedEvents))
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

        if (cursor is { Kind: TableRowKind.Event, Event: { } @event })
        {
            if (_rowIndexMap.TryGetValue(@event, out int index))
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

    private int ResolveCursorVisibleRow()
    {
        if (_cursor is { Kind: TableRowKind.Header, GroupKey: { } key })
        {
            return _rowView?.VisibleRowForHeader(key) ?? -1;
        }

        if (_cursor is { Kind: TableRowKind.Event, Event: { } @event } &&
            _rowIndexMap.TryGetValue(@event, out int index))
        {
            return _rowView?.VisibleRowForEvent(index) ?? index;
        }

        return -1;
    }

    private void ResortSelectionForCurrentTable()
    {
        DispatchSetSelection(_selectedEvents, ActiveEvent ?? _selectedEvent);
    }

    private async Task ScrollToSelectedEvent()
    {
        var target = ActiveEvent ?? _selectedEvent ?? _selectedEvents.LastOrDefault();

        if (target is null) { return; }

        var displayedEvents = _activeDisplayedEvents;

        if (displayedEvents.Count == 0) { return; }

        // Match OwningLog too so overlapping logs don't cross-match on RecordId.
        for (var index = 0; index < displayedEvents.Count; index++)
        {
            var candidate = displayedEvents[index];

            if (candidate.RecordId != target.RecordId ||
                !string.Equals(candidate.OwningLog, target.OwningLog, StringComparison.Ordinal) ||
                !string.Equals(candidate.LogName, target.LogName, StringComparison.Ordinal))
            {
                continue;
            }

            int targetRow = _rowView?.VisibleRowForEvent(index) ?? index;

            if (_tableModule is not null) { await _tableModule.InvokeVoidAsync("scrollToRow", targetRow); }

            return;
        }
    }

    private void SelectEvent(MouseEventArgs args, ResolvedEvent @event)
    {
        var displayedEvents = _activeDisplayedEvents;

        switch (args)
        {
            case { ShiftKey: true } when displayedEvents.Count > 0:
                if (_selectionAnchor is null)
                {
                    _selectionAnchor = @event;
                    SetCursorEvent(@event);
                    DispatchSetSelection([@event], @event);

                    return;
                }

                SetCursorEvent(@event);
                var range = BuildRange(displayedEvents, _selectionAnchor, @event);

                if (args.CtrlKey)
                {
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
                _selectionAnchor = @event;
                SetCursorEvent(@event);

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
                if (args.Button == 2 && _selectedSet.Contains(@event))
                {
                    SetCursorEvent(@event);
                    DispatchSetSelection(_selectedEvents, @event);

                    return;
                }

                _selectionAnchor = @event;
                SetCursorEvent(@event);
                DispatchSetSelection([@event], @event);

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

        var members = new List<ResolvedEvent>(group.EventCount);

        for (int i = group.StartIndex; i < group.StartIndex + group.EventCount; i++)
        {
            members.Add(_activeDisplayedEvents[i]);
        }

        var active = members[0];
        _selectionAnchor = active;
        SetCursorEvent(active);
        DispatchSetSelection(members, active);
    }

    private void SelectGroupByKey(string key)
    {
        if (_rowView is null || !_rowView.TryGetGroupByKey(key, out var group)) { return; }

        SelectGroup(group);
    }

    private void SetCursor(TableCursor? cursor) => _cursor = NormalizeCursor(cursor);

    private void SetCursorEvent(ResolvedEvent? @event) =>
        SetCursor(@event is null ? null : TableCursor.ForEvent(@event));

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
            MenuItem.Item("(none)", () => LogTableCommands.SetGroupBy(null), isChecked: state.GroupBy is null)
        };

        foreach (var (column, _) in state.Columns)
        {
            var capturedColumn = column;
            groupItems.Add(MenuItem.Item(
                column.ToFullString(),
                () => LogTableCommands.SetGroupBy(capturedColumn),
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

    private IReadOnlyList<MenuItem> ShowGroupContextMenuItems(EventGroup group)
    {
        bool collapsedNow = LogTableState.Value.IsGroupCollapsed(group.Key);

        return
        [
            MenuItem.Item(
                collapsedNow ? "Expand Group" : "Collapse Group",
                () => SetGroupCollapsed(group.Key, !collapsedNow)),
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

    private void ToggleGroupCollapsed(string groupKey) => LogTableCommands.ToggleGroupCollapsed(groupKey);

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
