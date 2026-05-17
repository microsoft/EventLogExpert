// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.UI.FilterCache;

internal sealed record LoadFiltersCompletedAction(
    ImmutableList<string> FavoriteFilters,
    ImmutableQueue<string> RecentFilters);
