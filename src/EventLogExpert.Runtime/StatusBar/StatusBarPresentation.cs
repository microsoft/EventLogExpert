// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.StatusBar;

public readonly record struct LoadingProgress(int Loaded, int Failed);

public sealed record StatusBarPresentation
{
    public bool ContinuouslyUpdate { get; init; }

    public int NewEventBufferCount { get; init; }

    public bool NewEventBufferIsFull { get; init; }

    public int SelectionCount { get; init; }

    public bool IsPersistentFilterActive { get; init; }

    public int RawEventTotal { get; init; }

    public ImmutableDictionary<EventLogId, ProviderResolutionCounts> RawEventCountsByLog { get; init; } =
        ImmutableDictionary<EventLogId, ProviderResolutionCounts>.Empty;

    public ImmutableDictionary<StatusActivityId, LoadingProgress> LoadingActivities { get; init; } =
        ImmutableDictionary<StatusActivityId, LoadingProgress>.Empty;

    public string ResolverStatus { get; init; } = string.Empty;

    public ImmutableList<LogView> Tabs { get; init; } = [];

    public ImmutableList<LogTabGroup> Groups { get; init; } = [];

    public EventLogId? ActiveTabId { get; init; }

    /// <summary>
    ///     Projects the per-log resolution tally for the active scope: the All-group sums every loaded log, a grouped tab
    ///     sums its members, and an ungrouped tab reads its own entry. Returns the full tally so the stats chip reads
    ///     <see cref="ProviderResolutionCounts.Total" /> and the coverage chip reads
    ///     <see cref="ProviderResolutionCounts.Unresolved" /> from one call.
    /// </summary>
    public ProviderResolutionCounts ScopedCounts(LogView activeTable)
    {
        ArgumentNullException.ThrowIfNull(activeTable);

        if (activeTable.GroupId?.IsAll == true)
        {
            return SumCounts(RawEventCountsByLog.Values);
        }

        if (activeTable.GroupId is not { } groupId)
        {
            return RawEventCountsByLog.GetValueOrDefault(activeTable.Id, default);
        }

        var group = Groups.FirstOrDefault(candidate => candidate.Id == groupId);

        return group is null ?
            default :
            SumCounts(group.MemberIds.Select(id => RawEventCountsByLog.GetValueOrDefault(id, default)));
    }

    private static ProviderResolutionCounts SumCounts(IEnumerable<ProviderResolutionCounts> counts)
    {
        ProviderResolutionCounts total = default;

        foreach (var value in counts)
        {
            total = total.Add(value);
        }

        return total;
    }
}
