// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.EventLog;

/// <summary>Coordinates log reload and database-removal lifecycle: query active presence, reload all, close-for-removal.</summary>
public interface ILogReloadCoordinator
{
    bool HasActiveLogs { get; }

    Task PrepareForDatabaseRemovalAsync(LogReopenSnapshot snapshot, CancellationToken cancellationToken = default);

    Task ReloadAllActiveLogsAsync(CancellationToken cancellationToken = default);

    void ReopenAfterDatabaseRemoval(IReadOnlyList<LogReopenInfo> snapshot);
}
