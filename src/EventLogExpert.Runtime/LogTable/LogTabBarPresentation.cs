// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.LogTable;

public sealed record LogTabBarPresentation
{
    public ImmutableList<LogView> Tabs { get; init; } = [];

    public ImmutableList<LogTabGroup> Groups { get; init; } = [];

    public EventLogId? ActiveTabId { get; init; }

    public ImmutableHashSet<EventLogId> KnownEmptyTabIds { get; init; } = ImmutableHashSet<EventLogId>.Empty;

    public bool HasMultipleTabs => Tabs.Count > 1;

    public bool IsKnownEmpty(EventLogId tabId) => KnownEmptyTabIds.Contains(tabId);
}
