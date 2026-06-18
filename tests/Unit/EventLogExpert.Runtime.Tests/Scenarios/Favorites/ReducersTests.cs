// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Scenarios.Favorites;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Tests.Scenarios.Favorites;

public sealed class ReducersTests
{
    [Fact]
    public void ReduceLoadScenarioFavoritesSuccess_ReplacesSetAndMarksLoaded()
    {
        var state = new ScenarioFavoritesState();
        var ids = ImmutableHashSet.Create("application-crashes", "failed-services-at-boot");

        var result = Reducers.ReduceLoadScenarioFavoritesSuccess(state, new LoadScenarioFavoritesSuccessAction(ids));

        Assert.Equal(ids, result.FavoriteScenarioIds);
        Assert.True(result.IsLoaded);
    }

    [Fact]
    public void ReduceSetScenarioFavoriteSuccess_Favorite_AddsId()
    {
        var state = new ScenarioFavoritesState { IsLoaded = true };

        var result = Reducers.ReduceSetScenarioFavoriteSuccess(
            state,
            new SetScenarioFavoriteSuccessAction("application-crashes", "Application crashes", IsFavorite: true));

        Assert.Contains("application-crashes", result.FavoriteScenarioIds);
        Assert.True(result.IsLoaded);
    }

    [Fact]
    public void ReduceSetScenarioFavoriteSuccess_Unfavorite_RemovesId()
    {
        var state = new ScenarioFavoritesState
        {
            FavoriteScenarioIds = ImmutableHashSet.Create("application-crashes", "failed-services-at-boot"),
            IsLoaded = true,
        };

        var result = Reducers.ReduceSetScenarioFavoriteSuccess(
            state,
            new SetScenarioFavoriteSuccessAction("application-crashes", "Application crashes", IsFavorite: false));

        Assert.DoesNotContain("application-crashes", result.FavoriteScenarioIds);
        Assert.Contains("failed-services-at-boot", result.FavoriteScenarioIds);
    }
}
