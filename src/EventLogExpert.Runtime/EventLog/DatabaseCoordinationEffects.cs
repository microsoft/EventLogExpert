// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Logging.Abstractions;
using Fluxor;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Runtime.EventLog;

internal sealed class DatabaseCoordinationEffects(
    IState<EventLogState> eventLogState,
    ITraceLogger logger,
    LogCloseCoordinator closeCoordinator,
    IDispatcher dispatcher) : ILogReloadCoordinator
{
    private readonly LogCloseCoordinator _closeCoordinator = closeCoordinator;
    private readonly IDispatcher _dispatcher = dispatcher;
    private readonly IState<EventLogState> _eventLogState = eventLogState;
    private readonly ITraceLogger _logger = logger;

    public async Task PrepareForDatabaseRemovalAsync(
        LogReopenSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var activeLogs = _eventLogState.Value.ActiveLogs.Values.ToList();

        if (activeLogs.Count == 0) { return; }

        await _closeCoordinator.AcquireCoordinatorLockAsync(cancellationToken);

        try
        {
            var reloadNames = activeLogs.Select(l => l.Name).ToHashSet(StringComparer.Ordinal);

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

            var waiters = new List<(EventLogId Id, string Name, LogPathType Type, Task Task)>(activeLogs.Count);

            foreach (var log in activeLogs)
            {
                var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _closeCoordinator.RegisterCloseCompletion(log.Id, tcs);
                waiters.Add((log.Id, log.Name, log.Type, tcs.Task));
            }

            foreach (var (id, name, _, _) in waiters)
            {
                _dispatcher.Dispatch(new CloseLogAction(id, name));
            }

            foreach (var (id, name, type, task) in waiters)
            {
                try
                {
                    await task.WaitAsync(LogCloseCoordinator.LogCloseTimeout, cancellationToken);

                    snapshot.Add(new LogReopenInfo(name, type));
                }
                catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
                {
                    _closeCoordinator.RemoveStrandedCompletion(id);

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

    public void ReopenAfterDatabaseRemoval(IReadOnlyList<LogReopenInfo> snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        foreach (var entry in snapshot)
        {
            _dispatcher.Dispatch(new OpenLogAction(entry.Name, entry.Type));
        }
    }
}
