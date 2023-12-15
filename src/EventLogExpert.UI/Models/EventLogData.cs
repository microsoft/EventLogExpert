// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using System.Collections.Immutable;
using System.Collections.ObjectModel;

namespace EventLogExpert.UI.Models;

public sealed record EventLogData(
    string Name,
    LogType Type,
    ReadOnlyCollection<DisplayEventModel> Events,
    ImmutableHashSet<int> EventIds,
    ImmutableHashSet<Guid?> EventActivityIds,
    ImmutableHashSet<string> EventProviderNames,
    ImmutableHashSet<string> TaskNames,
    ImmutableHashSet<string> KeywordNames);
