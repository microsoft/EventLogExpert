// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using System.Collections.Concurrent;

namespace EventLogExpert.Runtime.EventLog;

internal sealed class LogCloseCoordinator
{
    public static readonly TimeSpan LogCloseTimeout = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<EventLogId, TaskCompletionSource> _logCloseCompletions = new();
    private readonly SemaphoreSlim _logCloseCoordinatorLock = new(1, 1);
    private readonly ConcurrentDictionary<string, PendingSelectionRestore> _pendingSelectionRestore = new();

    public async Task AcquireCoordinatorLockAsync(CancellationToken cancellationToken = default) =>
        await _logCloseCoordinatorLock.WaitAsync(cancellationToken);

    public void ReleaseCoordinatorLock() => _logCloseCoordinatorLock.Release();

    public void RegisterCloseCompletion(EventLogId logId, TaskCompletionSource tcs) =>
        _logCloseCompletions[logId] = tcs;

    public void CompleteCloseFor(EventLogId logId)
    {
        if (_logCloseCompletions.TryRemove(logId, out var closeCompletion))
        {
            closeCompletion.TrySetResult();
        }
    }

    public void RemoveStrandedCompletion(EventLogId logId) => _logCloseCompletions.TryRemove(logId, out _);

    public void WritePendingRestore(string logName, PendingSelectionRestore restore) =>
        _pendingSelectionRestore[logName] = restore;

    public bool TryConsumePendingRestore(string logName, out PendingSelectionRestore? restore) =>
        _pendingSelectionRestore.TryRemove(logName, out restore);

    public void ClearPendingRestore(string logName) => _pendingSelectionRestore.TryRemove(logName, out _);

    public void ClearAllPendingRestore() => _pendingSelectionRestore.Clear();
}
