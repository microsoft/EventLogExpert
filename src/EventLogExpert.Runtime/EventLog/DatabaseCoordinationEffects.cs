// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Logging.Abstractions;
using Fluxor;
using System.Runtime.ExceptionServices;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Runtime.EventLog;

internal sealed class DatabaseCoordinationEffects(
    IState<EventLogState> eventLogState,
    ITraceLogger logger,
    LogCloseCoordinator closeCoordinator,
    IDispatcher dispatcher,
    IEventLogCommands eventLogCommands) : ILogReloadCoordinator
{
    private readonly LogCloseCoordinator _closeCoordinator = closeCoordinator;
    private readonly IDispatcher _dispatcher = dispatcher;
    private readonly IEventLogCommands _eventLogCommands = eventLogCommands;
    private readonly IState<EventLogState> _eventLogState = eventLogState;
    private readonly ITraceLogger _logger = logger;

    public bool HasActiveLogs => !_eventLogState.Value.OpenLogs.IsEmpty;

    public async Task PrepareForDatabaseRemovalAsync(
        LogReopenSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        cancellationToken.ThrowIfCancellationRequested();

        var openLogs = _eventLogState.Value.OpenLogs
            .Select(entry => (entry.Value.Id, Name: entry.Key, entry.Value.Type))
            .ToList();

        if (openLogs.Count == 0) { return; }

        await _closeCoordinator.AcquireCoordinatorLockAsync(cancellationToken);

        try
        {
            var reloadNames = openLogs.Select(l => l.Name).ToHashSet(StringComparer.Ordinal);

            var selectionByLog = _eventLogState.Value.SelectedEvents
                .Where(e => e.RecordId.HasValue && reloadNames.Contains(e.OwningLog))
                .GroupBy(e => e.OwningLog)
                .ToDictionary(g => g.Key, g => (IReadOnlySet<long>)g.Select(e => e.RecordId!.Value).ToHashSet());

            var selectedRecordId = _eventLogState.Value.SelectedEvent?.RecordId;
            var selectedLogName = _eventLogState.Value.SelectedEvent?.OwningLog;

            if (selectedRecordId.HasValue &&
                !string.IsNullOrEmpty(selectedLogName) &&
                reloadNames.Contains(selectedLogName) &&
                !selectionByLog.ContainsKey(selectedLogName))
            {
                selectionByLog[selectedLogName] = new HashSet<long>();
            }

            var waiters = new List<(EventLogId Id, string Name, LogPathType Type, Task Task)>(openLogs.Count);

            foreach (var log in openLogs)
            {
                var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _closeCoordinator.RegisterCloseCompletion(log.Id, tcs);
                waiters.Add((log.Id, log.Name, log.Type, tcs.Task));
            }

            foreach (var (id, name, _, _) in waiters)
            {
                _dispatcher.Dispatch(new CloseLogAction(id, name));
            }

            foreach (var (_, name, type, task) in waiters)
            {
                try
                {
                    await task.WaitAsync(LogCloseCoordinator.LogCloseTimeout, cancellationToken);

                    snapshot.Add(new LogReopenInfo(name, type));
                }
                catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
                {
                    foreach (var (strandedId, _, _, _) in waiters)
                    {
                        _closeCoordinator.RemoveStrandedCompletion(strandedId);
                    }

                    var alreadySnapshotted = new HashSet<string>(
                        snapshot.Items.Select(i => i.Name),
                        StringComparer.Ordinal);

                    foreach (var (_, dispatchedName, dispatchedType, _) in waiters)
                    {
                        if (alreadySnapshotted.Add(dispatchedName))
                        {
                            snapshot.Add(new LogReopenInfo(dispatchedName, dispatchedType));
                        }
                    }

                    _logger.Trace(
                        $"{nameof(PrepareForDatabaseRemovalAsync)}: close for log '{name}' did not complete: {ex.GetType().Name}");

                    throw;
                }
            }

            foreach (var (name, ids) in selectionByLog)
            {
                long? selectedIdForLog = string.Equals(name, selectedLogName, StringComparison.Ordinal) ?
                    selectedRecordId : null;

                _closeCoordinator.WritePendingRestore(name, new PendingSelectionRestore(ids, selectedIdForLog));
            }
        }
        finally
        {
            _closeCoordinator.ReleaseCoordinatorLock();
        }
    }

    public async Task ReloadAllActiveLogsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var openLogsBeforeClose = _eventLogState.Value.OpenLogs
            .Select(entry => (entry.Value.Id, Name: entry.Key, entry.Value.Type))
            .ToArray();

        if (openLogsBeforeClose.Length == 0) { return; }

        await _closeCoordinator.AcquireCoordinatorLockAsync(cancellationToken);

        ExceptionDispatchInfo? deferredCloseFailure = null;

        try
        {
            var waiters = new List<(EventLogId Id, string Name, Task Task)>(openLogsBeforeClose.Length);

            foreach (var log in openLogsBeforeClose)
            {
                var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _closeCoordinator.RegisterCloseCompletion(log.Id, tcs);
                waiters.Add((log.Id, log.Name, tcs.Task));
            }

            foreach (var (id, name, _) in waiters)
            {
                _dispatcher.Dispatch(new CloseLogAction(id, name));
            }

            foreach (var (_, name, task) in waiters)
            {
                try
                {
                    await task.WaitAsync(LogCloseCoordinator.LogCloseTimeout, cancellationToken);
                }
                catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
                {
                    foreach (var (strandedId, _, _) in waiters)
                    {
                        _closeCoordinator.RemoveStrandedCompletion(strandedId);
                    }

                    _logger.Trace(
                        $"{nameof(ReloadAllActiveLogsAsync)}: close for log '{name}' did not complete: {ex.GetType().Name}");

                    deferredCloseFailure = ExceptionDispatchInfo.Capture(ex);

                    break;
                }
            }
        }
        finally
        {
            _closeCoordinator.ReleaseCoordinatorLock();
        }

        foreach (var log in openLogsBeforeClose)
        {
            _eventLogCommands.OpenLog(log.Name, log.Type, CancellationToken.None);
        }

        deferredCloseFailure?.Throw();
    }

    public void ReopenAfterDatabaseRemoval(IReadOnlyList<LogReopenInfo> snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        foreach (var entry in snapshot)
        {
            _dispatcher.Dispatch(new OpenLogAction(entry.Name, entry.Type));
        }
    }
}
