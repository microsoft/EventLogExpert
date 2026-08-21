// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Memory;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.StatusBar;

internal sealed class StatusBarSource : IStatusBarSource, IDisposable
{
    private readonly IState<EventLogState> _eventLogState;
    private readonly IState<FilterPaneState> _filterPaneState;
    private readonly Lock _gate = new();
    private readonly IState<LogTableState> _logTableState;
    private readonly ITraceLogger _logger;
    private readonly IState<MemoryIndicatorState> _memoryState;
    private readonly IState<RawEventCountState> _rawCountState;
    private readonly IState<StatusBarState> _statusBarState;

    private bool _disposed;
    private (bool ContinuouslyUpdate, int BufferCount, bool BufferFull, int SelectionCount) _eventLogFacets;
    private (ImmutableList<LogView> Tabs, ImmutableList<LogTabGroup> Groups, EventLogId? ActiveTabId) _logTableFacets;
    private (long UsedMebibytes, long WorkingSetBytes, MemoryUsageLevel Level) _memoryFacets;
    private bool _persistentFilterActive;
    private (int Total, ImmutableDictionary<EventLogId, ProviderResolutionCounts> ByLog) _rawCountFacets;
    private (ImmutableDictionary<StatusActivityId, (int, int)> Loading, string Resolver) _statusFacets;

    public StatusBarSource(
        IState<EventLogState> eventLogState,
        IState<FilterPaneState> filterPaneState,
        IState<RawEventCountState> rawCountState,
        IState<StatusBarState> statusBarState,
        IState<LogTableState> logTableState,
        IState<MemoryIndicatorState> memoryState,
        [FromKeyedServices(LogCategories.EventLog)] ITraceLogger logger)
    {
        ArgumentNullException.ThrowIfNull(eventLogState);
        ArgumentNullException.ThrowIfNull(filterPaneState);
        ArgumentNullException.ThrowIfNull(rawCountState);
        ArgumentNullException.ThrowIfNull(statusBarState);
        ArgumentNullException.ThrowIfNull(logTableState);
        ArgumentNullException.ThrowIfNull(memoryState);
        ArgumentNullException.ThrowIfNull(logger);

        _eventLogState = eventLogState;
        _filterPaneState = filterPaneState;
        _rawCountState = rawCountState;
        _statusBarState = statusBarState;
        _logTableState = logTableState;
        _memoryState = memoryState;
        _logger = logger;

        SeedFacets();
        
        _eventLogState.StateChanged += OnEventLogChanged;
        _filterPaneState.StateChanged += OnFilterPaneChanged;
        _rawCountState.StateChanged += OnRawCountChanged;
        _statusBarState.StateChanged += OnStatusBarChanged;
        _logTableState.StateChanged += OnLogTableChanged;
        _memoryState.StateChanged += OnMemoryChanged;

        lock (_gate) { SeedFacets(); }
    }

    public event Action? Changed;

    public StatusBarPresentation Current => Project(
        _eventLogState.Value,
        _filterPaneState.Value,
        _rawCountState.Value,
        _statusBarState.Value,
        _logTableState.Value,
        _memoryState.Value);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) { return; }

            _disposed = true;
        }

        _eventLogState.StateChanged -= OnEventLogChanged;
        _filterPaneState.StateChanged -= OnFilterPaneChanged;
        _rawCountState.StateChanged -= OnRawCountChanged;
        _statusBarState.StateChanged -= OnStatusBarChanged;
        _logTableState.StateChanged -= OnLogTableChanged;
        _memoryState.StateChanged -= OnMemoryChanged;
    }

    private static bool DictEquals<TKey, TValue>(
        ImmutableDictionary<TKey, TValue> left,
        ImmutableDictionary<TKey, TValue> right)
        where TKey : notnull
    {
        if (ReferenceEquals(left, right)) { return true; }

        if (left.Count != right.Count) { return false; }

        foreach (var pair in left)
        {
            if (!right.TryGetValue(pair.Key, out var value) ||
                !EqualityComparer<TValue>.Default.Equals(value, pair.Value))
            {
                return false;
            }
        }

        return true;
    }

    private static StatusBarPresentation Project(
        EventLogState eventLog,
        FilterPaneState filterPane,
        RawEventCountState rawCount,
        StatusBarState statusBar,
        LogTableState logTable,
        MemoryIndicatorState memory) =>
        new()
        {
            ContinuouslyUpdate = eventLog.ContinuouslyUpdate,
            NewEventBufferCount = eventLog.NewEventBuffer.Count,
            NewEventBufferIsFull = eventLog.NewEventBufferIsFull,
            SelectionCount = eventLog.Selection.Count,
            IsPersistentFilterActive = filterPane.IsFilteringEnabled,
            RawEventTotal = rawCount.Total,
            RawEventCountsByLog = rawCount.ByLog,
            LoadingActivities = statusBar.EventsLoading.ToImmutableDictionary(
                pair => pair.Key,
                pair => new LoadingProgress(pair.Value.Item1, pair.Value.Item2)),
            ResolverStatus = statusBar.ResolverStatus,
            Tabs = logTable.EventTables,
            Groups = logTable.Groups,
            ActiveTabId = logTable.ActiveEventLogId,
            MemoryUsedMebibytes = memory.UsedMebibytes,
            MemoryWorkingSetBytes = memory.WorkingSetBytes,
            MemoryLevel = memory.Level
        };

    private static (bool, int, bool, int) ProjectEventLog(EventLogState state) =>
        (state.ContinuouslyUpdate, state.NewEventBuffer.Count, state.NewEventBufferIsFull, state.Selection.Count);

    private static (ImmutableList<LogView>, ImmutableList<LogTabGroup>, EventLogId?) ProjectLogTable(
        LogTableState state) =>
        (state.EventTables, state.Groups, state.ActiveEventLogId);

    private static (long, long, MemoryUsageLevel) ProjectMemory(MemoryIndicatorState state) =>
        (state.UsedMebibytes, state.WorkingSetBytes, state.Level);

    private static (int, ImmutableDictionary<EventLogId, ProviderResolutionCounts>) ProjectRawCount(RawEventCountState state) =>
        (state.Total, state.ByLog);

    private static (ImmutableDictionary<StatusActivityId, (int, int)>, string) ProjectStatus(StatusBarState state) =>
        (state.EventsLoading, state.ResolverStatus);

    private void OnEventLogChanged(object? sender, EventArgs e)
    {
        var next = ProjectEventLog(_eventLogState.Value);

        lock (_gate)
        {
            if (_disposed || next == _eventLogFacets) { return; }

            _eventLogFacets = next;
        }

        RaiseChanged();
    }

    private void OnFilterPaneChanged(object? sender, EventArgs e)
    {
        var next = _filterPaneState.Value.IsFilteringEnabled;

        lock (_gate)
        {
            if (_disposed || next == _persistentFilterActive) { return; }

            _persistentFilterActive = next;
        }

        RaiseChanged();
    }

    private void OnLogTableChanged(object? sender, EventArgs e)
    {
        var next = ProjectLogTable(_logTableState.Value);

        lock (_gate)
        {
            if (_disposed ||
                (ReferenceEquals(next.Item1, _logTableFacets.Tabs) &&
                    ReferenceEquals(next.Item2, _logTableFacets.Groups) &&
                    next.Item3 == _logTableFacets.ActiveTabId))
            {
                return;
            }

            _logTableFacets = next;
        }

        RaiseChanged();
    }

    private void OnMemoryChanged(object? sender, EventArgs e)
    {
        var next = ProjectMemory(_memoryState.Value);

        lock (_gate)
        {
            if (_disposed || next == _memoryFacets) { return; }

            _memoryFacets = next;
        }

        RaiseChanged();
    }

    private void OnRawCountChanged(object? sender, EventArgs e)
    {
        var next = ProjectRawCount(_rawCountState.Value);

        lock (_gate)
        {
            if (_disposed || (next.Item1 == _rawCountFacets.Total && DictEquals(next.Item2, _rawCountFacets.ByLog)))
            {
                return;
            }

            _rawCountFacets = next;
        }

        RaiseChanged();
    }

    private void OnStatusBarChanged(object? sender, EventArgs e)
    {
        var next = ProjectStatus(_statusBarState.Value);

        lock (_gate)
        {
            if (_disposed ||
                (next.Item2 == _statusFacets.Resolver && DictEquals(next.Item1, _statusFacets.Loading)))
            {
                return;
            }

            _statusFacets = next;
        }

        RaiseChanged();
    }

    private void RaiseChanged()
    {
        var handlers = Changed;

        if (handlers is null) { return; }

        foreach (var handler in handlers.GetInvocationList().Cast<Action>())
        {
            try { handler(); }
            catch (Exception fault) { _logger.Trace($"{nameof(StatusBarSource)}: a subscriber threw and was isolated: {fault}"); }
        }
    }

    private void SeedFacets()
    {
        _eventLogFacets = ProjectEventLog(_eventLogState.Value);
        _persistentFilterActive = _filterPaneState.Value.IsFilteringEnabled;
        _rawCountFacets = ProjectRawCount(_rawCountState.Value);
        _statusFacets = ProjectStatus(_statusBarState.Value);
        _logTableFacets = ProjectLogTable(_logTableState.Value);
        _memoryFacets = ProjectMemory(_memoryState.Value);
    }
}
