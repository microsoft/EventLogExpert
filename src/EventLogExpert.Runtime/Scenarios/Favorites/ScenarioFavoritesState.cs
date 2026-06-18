// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Scenarios.Favorites;

[FeatureState]
public sealed record ScenarioFavoritesState
{
    public ImmutableHashSet<string> FavoriteScenarioIds { get; init; } = [];

    public bool IsLoaded { get; init; }
}
