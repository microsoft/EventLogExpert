// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Common.Sources;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Scenarios.Favorites;

internal sealed class ScenarioFavoritesSource(IState<ScenarioFavoritesState> state, ITraceLogger logger)
    : ObservableStateSourceBase<ScenarioFavoritesState, ImmutableHashSet<string>>(
            state,
            logger,
            static state => state.FavoriteScenarioIds,
            static (next, current) => ReferenceEquals(next, current) || next.SetEquals(current)),
        IScenarioFavoritesSource
{
    public ImmutableHashSet<string> FavoriteScenarioIds => CurrentProjection;
}
