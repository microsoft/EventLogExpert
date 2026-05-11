// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.UI.FilterCache;

public sealed record RemoveFavoriteFilterCompletedAction(
    ImmutableList<string> FavoriteFilters,
    ImmutableQueue<string> RecentFilters);
