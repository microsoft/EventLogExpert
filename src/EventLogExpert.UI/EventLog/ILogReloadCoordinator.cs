// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.EventLog;

/// <summary>
///     Bridges DatabaseService delete operations and the EventLog Effects log lifecycle. Lets the database layer ask
///     the log layer to close every active log (so SQLite handles are released) and later reopen exactly the logs that
///     closed cleanly. Implemented by EventLog Effects so close/open dispatches share the same TCS dictionaries that
///     already track per-log load and close completion.
/// </summary>
public interface ILogReloadCoordinator
{
    Task PrepareForDatabaseRemovalAsync(LogReopenSnapshot snapshot, CancellationToken cancellationToken = default);

    void ReopenAfterDatabaseRemoval(IReadOnlyList<LogReopenInfo> snapshot);
}
