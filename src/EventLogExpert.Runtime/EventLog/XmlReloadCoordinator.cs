// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Logging.Abstractions;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Runtime.EventLog;

internal sealed class XmlReloadCoordinator(
    IState<EventLogState> eventLogState,
    LogCloseCoordinator closeCoordinator,
    EventLogConcurrencyState concurrencyState,
    [FromKeyedServices(LogCategories.EventLog)] ITraceLogger logger)
{
    private readonly LogCloseCoordinator _closeCoordinator = closeCoordinator;
    private readonly EventLogConcurrencyState _concurrencyState = concurrencyState;
    private readonly IState<EventLogState> _eventLogState = eventLogState;
    private readonly ITraceLogger _logger = logger;

    public async Task ReloadAsync(PendingXmlReload pending, IDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(pending);
        ArgumentNullException.ThrowIfNull(dispatcher);

        var logsNeedingReload = pending.Logs;
        long reloadToken = pending.ReloadToken;
        var reloadNames = logsNeedingReload.Select(log => log.Name).ToHashSet(StringComparer.Ordinal);

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
                        $"{nameof(ReloadAsync)}: close for log '{name}' did not complete within {LogCloseCoordinator.LogCloseTimeout}; selection will not be restored to avoid race with the delayed close wiping the entry.");
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
                    $"{nameof(ReloadAsync)}: reload superseded by CloseAll; skipping reopen of {logsNeedingReload.Count} log(s) and clearing pending selection restore.");

                return;
            }

            var reopenedSoFar = new List<(EventLogId Id, string Name)>(logsNeedingReload.Count);

            void AbortReopenAsSuperseded(string when)
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
                    $"{nameof(ReloadAsync)}: reload superseded by CloseAll {when}; dispatched CloseLog for {reopenedSoFar.Count} reopened log(s) and cleared pending selection restore.");
            }

            foreach (var (_, name, type) in logsNeedingReload)
            {
                if (_concurrencyState.GetCurrentReloadToken() != reloadToken)
                {
                    AbortReopenAsSuperseded("mid-reopen");

                    return;
                }

                var reopenedId = EventLogId.Create();

                dispatcher.Dispatch(new OpenLogAction(name, type, PreassignedId: reopenedId));
                reopenedSoFar.Add((reopenedId, name));
            }

            if (_concurrencyState.GetCurrentReloadToken() != reloadToken)
            {
                AbortReopenAsSuperseded("after reopen");
            }
        }
        finally
        {
            _closeCoordinator.ReleaseCoordinatorLock();
        }
    }

    public PendingXmlReload Resolve(Filter filter)
    {
        long reloadToken = _concurrencyState.GetCurrentReloadToken();

        var logs = filter.RequiresXml && !_eventLogState.Value.OpenLogs.IsEmpty ?
            _eventLogState.Value.OpenLogs
                .Where(entry => !_concurrencyState.IsLoadedWithXml(entry.Value.Id))
                .Select(entry => (entry.Value.Id, Name: entry.Key, entry.Value.Type))
                .ToList() : [];

        return new PendingXmlReload(logs, reloadToken);
    }
}

internal sealed record PendingXmlReload(
    List<(EventLogId Id, string Name, LogPathType Type)> Logs,
    long ReloadToken)
{
    public bool IsNeeded => Logs.Count > 0;
}
