// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Memory;
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

    public long MemoryUsedBytes { get; init; }

    public long MemoryBudgetBytes { get; init; }

    public MemoryPressureLevel MemoryLevel { get; init; }

    public string? HeaviestMemoryLogName { get; init; }

    public ImmutableHashSet<EventLogId> PartiallyLoadedForMemory { get; init; } =
        ImmutableHashSet<EventLogId>.Empty;

    public ImmutableDictionary<EventLogId, int> RawEventCountsByLog { get; init; } =
        ImmutableDictionary<EventLogId, int>.Empty;

    public ImmutableDictionary<StatusActivityId, LoadingProgress> LoadingActivities { get; init; } =
        ImmutableDictionary<StatusActivityId, LoadingProgress>.Empty;

    public string ResolverStatus { get; init; } = string.Empty;

    public ImmutableList<LogView> Tabs { get; init; } = [];

    public ImmutableList<LogTabGroup> Groups { get; init; } = [];

    public EventLogId? ActiveTabId { get; init; }
}
