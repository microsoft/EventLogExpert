// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;

namespace EventLogExpert.Runtime.Scenarios.Favorites;

internal sealed class Reducers
{
    [ReducerMethod]
    public static ScenarioFavoritesState ReduceLoadScenarioFavoritesSuccess(
        ScenarioFavoritesState state,
        LoadScenarioFavoritesSuccessAction action) =>
        state with { FavoriteScenarioIds = action.FavoriteScenarioIds, IsLoaded = true };

    [ReducerMethod]
    public static ScenarioFavoritesState ReduceSetScenarioFavoriteSuccess(
        ScenarioFavoritesState state,
        SetScenarioFavoriteSuccessAction action) =>
        state with
        {
            FavoriteScenarioIds = action.IsFavorite
                ? state.FavoriteScenarioIds.Add(action.ScenarioId)
                : state.FavoriteScenarioIds.Remove(action.ScenarioId),
        };
}
