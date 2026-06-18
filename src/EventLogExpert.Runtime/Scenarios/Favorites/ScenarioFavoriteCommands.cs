// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;

namespace EventLogExpert.Runtime.Scenarios.Favorites;

internal sealed class ScenarioFavoriteCommands(IDispatcher dispatcher) : IScenarioFavoriteCommands
{
    public void Load() => dispatcher.Dispatch(new LoadScenarioFavoritesAction());

    public void SetFavorite(string scenarioId, string scenarioName, bool isFavorite) =>
        dispatcher.Dispatch(new SetScenarioFavoriteAction(scenarioId, scenarioName, isFavorite));
}
