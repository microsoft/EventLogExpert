// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Readers;

namespace EventLogExpert.UI.Store.EventLog;

/// <summary>
///     Bridges DatabaseService delete operations and the EventLogEffects log lifecycle. Lets the database layer ask
///     the log layer to close every active log (so SQLite handles are released) and later reopen exactly the logs that
///     closed cleanly. Implemented by EventLogEffects so close/open dispatches share the same TCS dictionaries that
///     already track per-log load and close completion.
/// </summary>
public interface ILogReloadCoordinator
{
    Task PrepareForDatabaseRemovalAsync(LogReopenSnapshot snapshot, CancellationToken cancellationToken = default);

    void ReopenAfterDatabaseRemoval(IReadOnlyList<LogReopenInfo> snapshot);
}

public sealed record LogReopenInfo(string Name, PathType Type);

/// <summary>
///     Mutable container that the coordinator populates as each active log finishes closing. Callers pass an empty
///     snapshot in to <see cref="ILogReloadCoordinator.PrepareForDatabaseRemovalAsync" /> and then unconditionally pass
///     <see cref="Items" /> to <see cref="ILogReloadCoordinator.ReopenAfterDatabaseRemoval" /> in their <c>finally</c>
///     block — that way logs that closed cleanly always get reopened, even when a later phase (file delete, reservation,
///     etc.) throws before all logs were processed.
/// </summary>
public sealed class LogReopenSnapshot
{
    private readonly List<LogReopenInfo> _items = [];

    public IReadOnlyList<LogReopenInfo> Items => _items;

    internal void Add(LogReopenInfo info)
    {
        _items.Add(info);
    }
}
