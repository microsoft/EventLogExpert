// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Store.EventLog;

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
