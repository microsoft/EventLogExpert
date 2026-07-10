// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Compilation;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.FilterProgress;
using EventLogExpert.Runtime.LogTable;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Immutable;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Runtime.EventLog;

internal sealed class FilteringEffects(
    IState<EventLogState> eventLogState,
    IState<RawEventStoreState> rawEventStore,
    IState<LogTableState> logTableState,
    IFilterService filterService,
    [FromKeyedServices(LogCategories.EventLog)] ITraceLogger logger,
    LogCloseCoordinator closeCoordinator,
    EventLogConcurrencyState concurrencyState,
    TimeSpan? convergenceDelay = null)
{
    private readonly LogCloseCoordinator _closeCoordinator = closeCoordinator;
    private readonly EventLogConcurrencyState _concurrencyState = concurrencyState;
    private readonly TimeSpan _convergenceDelay = convergenceDelay ?? TimeSpan.FromMilliseconds(250);
    private readonly IState<EventLogState> _eventLogState = eventLogState;
    private readonly IFilterService _filterService = filterService;
    private readonly ITraceLogger _logger = logger;
    private readonly IState<LogTableState> _logTableState = logTableState;
    private readonly IState<RawEventStoreState> _rawEventStore = rawEventStore;

    [EffectMethod]
    public Task HandleAddEvent(AddEventAction action, IDispatcher dispatcher)
    {
        if (!_eventLogState.Value.OpenLogs.TryGetValue(action.NewEvent.OwningLog, out var owningLog))
        {
            return Task.CompletedTask;
        }

        if (_eventLogState.Value.ContinuouslyUpdate)
        {
            // Raw ingest is unconditional even when the filter hides the event; only the display rebuild is filter-gated.
            dispatcher.Dispatch(new IngestRawEventsAction(
                new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>> { [owningLog.Id] = [action.NewEvent] },
                RawIngestMode.Prepend));

            // Skip the rebuild when the new event is hidden: the view is unchanged, so avoid the version churn.
            var filteredNew = _filterService.GetFilteredEvents(
                [action.NewEvent],
                _eventLogState.Value.AppliedFilter);

            if (filteredNew.Count > 0 &&
                _rawEventStore.Value.ByLog.TryGetValue(owningLog.Id, out var store))
            {
                var view = DisplayViewBuilder.Build(
                    store, owningLog.Id, _eventLogState.Value.AppliedFilter, _logTableState.Value.SortContext);

                dispatcher.Dispatch(new AppendTableEventsAction(owningLog.Id) { View = view });
            }

            return Task.CompletedTask;
        }

        var updatedBuffer = new List<ResolvedEvent>(_eventLogState.Value.NewEventBuffer.Count + 1)
        {
            action.NewEvent
        };

        updatedBuffer.AddRange(_eventLogState.Value.NewEventBuffer);

        var full = updatedBuffer.Count >= EventLogState.MaxNewEvents;

        dispatcher.Dispatch(new EventBufferedAction(updatedBuffer.AsReadOnly(), full));

        return Task.CompletedTask;
    }

    [EffectMethod]
    public async Task HandleApplyFilter(ApplyFilterAction action, IDispatcher dispatcher)
    {
        long reloadTokenAtStart = _concurrencyState.GetCurrentReloadToken();

        bool newRequiresXml = action.Filter.RequiresXml;

        // Reopen only logs missing the XML the new filter needs; UserData filters resolve from stored fields and never force a reload.
        var logsNeedingReload = newRequiresXml && !_eventLogState.Value.OpenLogs.IsEmpty
            ? _eventLogState.Value.OpenLogs
                .Where(kvp => !_concurrencyState.IsLoadedWithXml(kvp.Value.Id))
                .Select(kvp => (kvp.Value.Id, Name: kvp.Key, kvp.Value.Type))
                .ToList()
            : [];

        long filterToken = _concurrencyState.InvalidateInFlightFilters();

        if (logsNeedingReload.Count > 0)
        {
            dispatcher.Dispatch(new SetFilterProgressAction(false));

            await ReloadLogsWithXmlAsync(logsNeedingReload, reloadTokenAtStart, dispatcher);

            return;
        }

        await ApplyFilterAndPublishAsync(action.Filter, filterToken, dispatcher);
    }

    [EffectMethod]
    public async Task HandleConvergeFilter(ConvergeFilterAction action, IDispatcher dispatcher)
    {
        long filterToken = action.OriginToken;

        if (_concurrencyState.GetCurrentFilterToken() != filterToken) { return; }

        var filter = _eventLogState.Value.AppliedFilter;
        var logTable = _logTableState.Value;
        var context = logTable.SortContext;
        var version = logTable.DisplayListVersion;

        var targets = ResidualOpenStale(action.StaleIds);

        if (targets.Length == 0)
        {
            if (_concurrencyState.GetCurrentFilterToken() == filterToken)
            {
                dispatcher.Dispatch(new SetFilterProgressAction(false));
            }

            return;
        }

        bool convergenceScheduled = false;

        try
        {
            var snapshot = SnapshotEventsForLogs(targets);
            var sorted = await Task.Run(() => FilterAndSort(snapshot, filter, context));

            if (_concurrencyState.GetCurrentFilterToken() != filterToken) { return; }

            var capturedByLog = snapshot.ToDictionary(pair => pair.Id, pair => pair.ContentVersion);
            var nowRaw = _rawEventStore.Value.ByLog;
            var fresh = new Dictionary<EventLogId, EventColumnView>(sorted.Count);
            var residual = new List<EventLogId>();

            foreach (var logId in targets)
            {
                // M1 race guard: an unchanged ContentVersion means no rebuild slipped in since the snapshot, so the built view still matches the live store.
                if (sorted.TryGetValue(logId, out var view) &&
                    capturedByLog.TryGetValue(logId, out var capturedVersion) &&
                    nowRaw.TryGetValue(logId, out var now) &&
                    now.ContentVersion == capturedVersion)
                {
                    fresh[logId] = view;
                }
                else
                {
                    residual.Add(logId);
                }
            }

            if (fresh.Count > 0)
            {
                dispatcher.Dispatch(new DisplayReadyAction { Views = fresh, Version = version });
            }

            var stillStale = ResidualOpenStale(residual);

            if (stillStale.Length > 0)
            {
                convergenceScheduled = true;

                await Task.Delay(_convergenceDelay);

                if (_concurrencyState.GetCurrentFilterToken() == filterToken)
                {
                    dispatcher.Dispatch(new ConvergeFilterAction(stillStale, filterToken));
                }
            }
        }
        finally
        {
            if (!convergenceScheduled && _concurrencyState.GetCurrentFilterToken() == filterToken)
            {
                dispatcher.Dispatch(new SetFilterProgressAction(false));
            }
        }
    }

    [EffectMethod]
    public Task HandleSetContinuouslyUpdate(SetContinuouslyUpdateAction action, IDispatcher dispatcher)
    {
        if (action.ContinuouslyUpdate)
        {
            LogReloadEffects.ProcessNewEventBuffer(
                _eventLogState.Value, _rawEventStore, _logTableState, dispatcher, _filterService);
        }

        return Task.CompletedTask;
    }

    [EffectMethod]
    public async Task HandleSetGroupBy(SetGroupByAction action, IDispatcher dispatcher) =>
        await RepublishForSortAsync(dispatcher);

    [EffectMethod]
    public async Task HandleSetOrderBy(SetOrderByAction action, IDispatcher dispatcher) =>
        await RepublishForSortAsync(dispatcher);

    [EffectMethod(typeof(ToggleGroupSortingAction))]
    public async Task HandleToggleGroupSorting(IDispatcher dispatcher)
    {
        if (_logTableState.Value.RequestedGroupBy is null) { return; }

        await RepublishForSortAsync(dispatcher);
    }

    [EffectMethod(typeof(ToggleSortingAction))]
    public async Task HandleToggleSorting(IDispatcher dispatcher) =>
        await RepublishForSortAsync(dispatcher);

    [EffectMethod(typeof(UpdateTableAction))]
    public async Task HandleUpdateTable(IDispatcher dispatcher)
    {
        // A reopen settles under the live sort and drops the in-flight rebuild; republish if a sort is still pending.
        if (!_logTableState.Value.HasPendingSortChange) { return; }

        await RepublishForSortAsync(dispatcher);
    }

    private static Dictionary<EventLogId, EventColumnView> FilterAndSort(
        IReadOnlyList<(EventLogId Id, EventColumnStore Store, long ContentVersion)> snapshot,
        Filter filter,
        SortContext context)
    {
        var views = new Dictionary<EventLogId, EventColumnView>(snapshot.Count);

        foreach (var (logId, store, _) in snapshot)
        {
            views[logId] = DisplayViewBuilder.Build(store, logId, filter, context);
        }

        return views;
    }

    private async Task ApplyFilterAndPublishAsync(Filter filter, long filterToken, IDispatcher dispatcher)
    {
        var snapshot = SnapshotOpenLogEvents();
        var logTable = _logTableState.Value;
        var capturedContext = logTable.SortContext;
        var version = logTable.DisplayListVersion;

        dispatcher.Dispatch(new SetFilterProgressAction(true));

        bool convergenceScheduled = false;

        try
        {
            var sortedActiveLogs = await Task.Run(() => FilterAndSort(snapshot, filter, capturedContext));

            if (_concurrencyState.GetCurrentFilterToken() != filterToken) { return; }

            var snapshotById = snapshot.ToDictionary(pair => pair.Id, pair => pair.ContentVersion);
            var currentRaw = _rawEventStore.Value.ByLog;
            var fresh = new Dictionary<EventLogId, EventColumnView>(sortedActiveLogs.Count);
            var staleIds = new List<EventLogId>();

            foreach (var (logId, view) in sortedActiveLogs)
            {
                if (!snapshotById.TryGetValue(logId, out var snapshotVersion)) { continue; }

                if (currentRaw.TryGetValue(logId, out var current) &&
                    current.ContentVersion == snapshotVersion)
                {
                    fresh[logId] = view;
                }
                else
                {
                    staleIds.Add(logId);
                }
            }

            if (staleIds.Count > 0)
            {
                var pass2Snapshot = SnapshotEventsForLogs(staleIds);
                var refilteredSorted = await Task.Run(() => FilterAndSort(pass2Snapshot, filter, capturedContext));

                if (_concurrencyState.GetCurrentFilterToken() != filterToken) { return; }

                var pass2CapturedById = pass2Snapshot.ToDictionary(pair => pair.Id, pair => pair.ContentVersion);
                var nowRaw = _rawEventStore.Value.ByLog;

                foreach (var (logId, view) in refilteredSorted)
                {
                    if (!pass2CapturedById.TryGetValue(logId, out var pass2Version)) { continue; }

                    if (nowRaw.TryGetValue(logId, out var now) &&
                        now.ContentVersion == pass2Version)
                    {
                        fresh[logId] = view;
                    }
                }
            }

            dispatcher.Dispatch(new DisplayReadyAction { Views = fresh, Version = version });

            var residualStale = ResidualOpenStale(staleIds.Where(id => !fresh.ContainsKey(id)));

            if (residualStale.Length > 0)
            {
                convergenceScheduled = true;

                await Task.Delay(_convergenceDelay);

                if (_concurrencyState.GetCurrentFilterToken() == filterToken)
                {
                    dispatcher.Dispatch(new ConvergeFilterAction(residualStale, filterToken));
                }
            }
        }
        finally
        {
            if (!convergenceScheduled && _concurrencyState.GetCurrentFilterToken() == filterToken)
            {
                dispatcher.Dispatch(new SetFilterProgressAction(false));
            }
        }
    }

    private async Task ReloadLogsWithXmlAsync(
        List<(EventLogId Id, string Name, LogPathType Type)> logsNeedingReload,
        long reloadToken,
        IDispatcher dispatcher)
    {
        var reloadNames = logsNeedingReload.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

        var selectionByLog = _eventLogState.Value.Selection
            .Where(entry => entry.ReloadKey is { } key && reloadNames.Contains(key.OwningLog))
            .GroupBy(entry => entry.ReloadKey!.Value.OwningLog)
            .ToDictionary(
                group => group.Key,
                IReadOnlySet<long> (group) => group.Select(entry => entry.ReloadKey!.Value.RecordId).ToHashSet());

        var focus = _eventLogState.Value.Focus;
        long? selectedRecordId = focus?.ReloadKey?.RecordId;
        string? selectedLogName = focus?.ReloadKey?.OwningLog;

        if (selectedRecordId.HasValue &&
            !string.IsNullOrEmpty(selectedLogName) &&
            reloadNames.Contains(selectedLogName) &&
            !selectionByLog.ContainsKey(selectedLogName))
        {
            selectionByLog[selectedLogName] = new HashSet<long>();
        }

        await _closeCoordinator.AcquireCoordinatorLockAsync();

        try
        {
            var closeWaiters = new List<(EventLogId Id, string Name, Task Task)>(logsNeedingReload.Count);

            foreach (var (id, name, _) in logsNeedingReload)
            {
                var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _closeCoordinator.RegisterCloseCompletion(id, tcs);
                closeWaiters.Add((id, name, tcs.Task));
            }

            foreach (var (id, name, _) in logsNeedingReload)
            {
                dispatcher.Dispatch(new CloseLogAction(id, name));
            }

            var timedOutLogs = new HashSet<string>(StringComparer.Ordinal);

            foreach (var (id, name, task) in closeWaiters)
            {
                try
                {
                    await task.WaitAsync(LogCloseCoordinator.LogCloseTimeout);
                }
                catch (TimeoutException)
                {
                    _closeCoordinator.RemoveStrandedCompletion(id);
                    timedOutLogs.Add(name);

                    _logger.Trace(
                        $"{nameof(HandleApplyFilter)}: close for log '{name}' did not complete within {LogCloseCoordinator.LogCloseTimeout}; selection will not be restored to avoid race with the delayed close wiping the entry.");
                }
            }

            foreach (var (name, ids) in selectionByLog)
            {
                if (timedOutLogs.Contains(name)) { continue; }

                long? selectedIdForLog = string.Equals(name, selectedLogName, StringComparison.Ordinal) ?
                    selectedRecordId : null;

                _closeCoordinator.WritePendingRestore(name, new PendingSelectionRestore(ids, selectedIdForLog));
            }

            if (_concurrencyState.GetCurrentReloadToken() != reloadToken)
            {
                foreach (var (_, name, _) in logsNeedingReload)
                {
                    _closeCoordinator.ClearPendingRestore(name);
                }

                _logger.Trace(
                    $"{nameof(HandleApplyFilter)}: reload superseded by CloseAll; skipping reopen of {logsNeedingReload.Count} log(s) and clearing pending selection restore.");

                return;
            }

            var reopenedSoFar = new List<(EventLogId Id, string Name)>(logsNeedingReload.Count);

            foreach (var (id, name, type) in logsNeedingReload)
            {
                if (_concurrencyState.GetCurrentReloadToken() != reloadToken)
                {
                    foreach (var (reopenedId, reopenedName) in reopenedSoFar)
                    {
                        dispatcher.Dispatch(new CloseLogAction(reopenedId, reopenedName));
                    }

                    foreach (var (_, restoreName, _) in logsNeedingReload)
                    {
                        _closeCoordinator.ClearPendingRestore(restoreName);
                    }

                    _logger.Trace(
                        $"{nameof(HandleApplyFilter)}: reload superseded by CloseAll mid-reopen; dispatched CloseLog for {reopenedSoFar.Count} just-reopened log(s) and cleared pending selection restore.");

                    return;
                }

                dispatcher.Dispatch(new OpenLogAction(name, type));
                reopenedSoFar.Add((id, name));
            }
        }
        finally
        {
            _closeCoordinator.ReleaseCoordinatorLock();
        }
    }

    private Task RepublishForSortAsync(IDispatcher dispatcher) =>
        ApplyFilterAndPublishAsync(
            _eventLogState.Value.AppliedFilter,
            _concurrencyState.InvalidateInFlightFilters(),
            dispatcher);

    private ImmutableArray<EventLogId> ResidualOpenStale(IEnumerable<EventLogId> candidateStaleIds)
    {
        var openIds = new HashSet<EventLogId>();

        foreach (var info in _eventLogState.Value.OpenLogs.Values) { openIds.Add(info.Id); }

        var raw = _rawEventStore.Value.ByLog;

        return [.. candidateStaleIds.Distinct().Where(id => openIds.Contains(id) && raw.ContainsKey(id))];
    }

    private List<(EventLogId Id, EventColumnStore Store, long ContentVersion)> SnapshotEventsForLogs(
        IReadOnlyList<EventLogId> logIds)
    {
        var raw = _rawEventStore.Value.ByLog;
        var snapshot = new List<(EventLogId Id, EventColumnStore Store, long ContentVersion)>();

        foreach (var logId in logIds)
        {
            if (raw.TryGetValue(logId, out var store))
            {
                snapshot.Add((logId, store, store.ContentVersion));
            }
        }

        return snapshot;
    }

    private List<(EventLogId Id, EventColumnStore Store, long ContentVersion)> SnapshotOpenLogEvents()
    {
        var raw = _rawEventStore.Value.ByLog;
        var snapshot = new List<(EventLogId Id, EventColumnStore Store, long ContentVersion)>();

        foreach (var info in _eventLogState.Value.OpenLogs.Values)
        {
            if (raw.TryGetValue(info.Id, out var store))
            {
                snapshot.Add((info.Id, store, store.ContentVersion));
            }
        }

        return snapshot;
    }
}
