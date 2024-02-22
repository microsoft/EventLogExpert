// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using System.Collections.Immutable;
using System.Collections.ObjectModel;

namespace EventLogExpert.UI.Models;

public readonly record struct EventLogData(
    string Name,
    LogType Type,
    ReadOnlyCollection<DisplayEventModel> Events,
    ImmutableHashSet<int> EventIds,
    ImmutableHashSet<Guid?> EventActivityIds,
    ImmutableHashSet<string> EventProviderNames,
    ImmutableHashSet<string> TaskNames,
    ImmutableHashSet<string> KeywordNames)
{
    public EventLogId Id { get; } = EventLogId.Create();
}
