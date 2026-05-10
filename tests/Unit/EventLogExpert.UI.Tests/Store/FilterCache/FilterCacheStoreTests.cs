// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Store.FilterCache;
using EventLogExpert.UI.Tests.TestUtils.Constants;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Tests.Store.FilterCache;

public sealed class FilterCacheStoreTests
{
    [Fact]
    public void FilterCacheAction_AddFavoriteFilterCompleted_ShouldStoreFilters()
    {
        // Arrange
        var filters = ImmutableList.Create(Constants.FilterIdEquals100, Constants.FilterIdEquals200);

        // Act
        var action = new AddFavoriteFilterCompletedAction(filters);

        // Assert
        Assert.Equal(2, action.Filters.Count);
        Assert.Contains(Constants.FilterIdEquals100, action.Filters);
        Assert.Contains(Constants.FilterIdEquals200, action.Filters);
    }

    [Fact]
    public void FilterCacheAction_AddFavoriteFilter_ShouldStoreFilter()
    {
        // Arrange
        var filter = Constants.FilterIdEquals100;

        // Act
        var action = new AddFavoriteFilterAction(filter);

        // Assert
        Assert.Equal(filter, action.Filter);
    }

    [Fact]
    public void FilterCacheAction_AddRecentFilterCompleted_ShouldStoreFilters()
    {
        // Arrange
        var filters = ImmutableQueue.Create(Constants.FilterIdEquals100, Constants.FilterIdEquals200);

        // Act
        var action = new AddRecentFilterCompletedAction(filters);

        // Assert
        Assert.Equal(2, action.Filters.Count());
    }

    [Fact]
    public void FilterCacheAction_AddRecentFilter_ShouldStoreFilter()
    {
        // Arrange
        var filter = Constants.FilterLevelEqualsError;

        // Act
        var action = new AddRecentFilterAction(filter);

        // Assert
        Assert.Equal(filter, action.Filter);
    }

    [Fact]
    public void FilterCacheAction_ImportFavorites_ShouldStoreFilters()
    {
        // Arrange
        var filters = new List<string> { Constants.FilterIdEquals100, Constants.FilterIdEquals200 };

        // Act
        var action = new ImportFavoritesAction(filters);

        // Assert
        Assert.Equal(2, action.Filters.Count);
    }

    [Fact]
    public void FilterCacheAction_LoadFiltersCompleted_ShouldStoreBothFilters()
    {
        // Arrange
        var favorites = ImmutableList.Create(Constants.FilterIdEquals100);
        var recent = ImmutableQueue.Create(Constants.FilterIdEquals200);

        // Act
        var action = new LoadFiltersCompletedAction(favorites, recent);

        // Assert
        Assert.Single(action.FavoriteFilters);
        Assert.Single(action.RecentFilters);
    }

    [Fact]
    public void FilterCacheAction_RemoveFavoriteFilterCompleted_ShouldStoreBothFilters()
    {
        // Arrange
        var favorites = ImmutableList.Create(Constants.FilterIdEquals100);
        var recent = ImmutableQueue.Create(Constants.FilterIdEquals200);

        // Act
        var action = new RemoveFavoriteFilterCompletedAction(favorites, recent);

        // Assert
        Assert.Single(action.FavoriteFilters);
        Assert.Single(action.RecentFilters);
    }

    [Fact]
    public void FilterCacheAction_RemoveFavoriteFilter_ShouldStoreFilter()
    {
        // Arrange
        var filter = Constants.FilterIdEquals100;

        // Act
        var action = new RemoveFavoriteFilterAction(filter);

        // Assert
        Assert.Equal(filter, action.Filter);
    }

    [Fact]
    public void FilterCacheState_DefaultState_ShouldHaveCorrectDefaults()
    {
        // Arrange & Act
        var state = new FilterCacheState();

        // Assert
        Assert.Empty(state.FavoriteFilters);
        Assert.Empty(state.RecentFilters);
    }

    [Fact]
    public void IntegrationTest_AddMultipleFavorites()
    {
        // Arrange
        var state = new FilterCacheState();

        // Act - Add first favorite
        var favorites1 = ImmutableList.Create(Constants.FilterIdEquals100);

        state = Reducers.ReduceAddFavoriteFilterCompleted(
            state,
            new AddFavoriteFilterCompletedAction(favorites1));

        // Assert
        Assert.Single(state.FavoriteFilters);

        // Act - Add second favorite
        var favorites2 = favorites1.Add(Constants.FilterIdEquals200);

        state = Reducers.ReduceAddFavoriteFilterCompleted(
            state,
            new AddFavoriteFilterCompletedAction(favorites2));

        // Assert
        Assert.Equal(2, state.FavoriteFilters.Count);
        Assert.Contains(Constants.FilterIdEquals100, state.FavoriteFilters);
        Assert.Contains(Constants.FilterIdEquals200, state.FavoriteFilters);
    }

    [Fact]
    public void IntegrationTest_AddMultipleRecent()
    {
        // Arrange
        var state = new FilterCacheState();

        // Act - Add first recent
        var recent1 = ImmutableQueue.Create(Constants.FilterIdEquals100);

        state = Reducers.ReduceAddRecentFilterCompleted(
            state,
            new AddRecentFilterCompletedAction(recent1));

        // Assert
        Assert.Single(state.RecentFilters);

        // Act - Add second recent
        var recent2 = recent1.Enqueue(Constants.FilterIdEquals200);

        state = Reducers.ReduceAddRecentFilterCompleted(
            state,
            new AddRecentFilterCompletedAction(recent2));

        // Assert
        Assert.Equal(2, state.RecentFilters.Count());
        Assert.Equal(Constants.FilterIdEquals100, state.RecentFilters.First());
    }

    [Fact]
    public void IntegrationTest_ComplexFilterManagement()
    {
        // Arrange
        var state = new FilterCacheState();

        // Act - Load initial state
        var initialFavorites = ImmutableList.Create(
            Constants.FilterIdEquals100,
            Constants.FilterIdEquals200);

        var initialRecent = ImmutableQueue.Create(Constants.FilterLevelEqualsError);

        state = Reducers.ReduceLoadFiltersCompleted(
            state,
            new LoadFiltersCompletedAction(initialFavorites, initialRecent));

        // Assert initial state
        Assert.Equal(2, state.FavoriteFilters.Count);
        Assert.Single(state.RecentFilters);

        // Act - Add new favorite
        var updatedFavorites = state.FavoriteFilters.Add(Constants.FilterIdGreaterThan100);

        state = Reducers.ReduceAddFavoriteFilterCompleted(
            state,
            new AddFavoriteFilterCompletedAction(updatedFavorites));

        // Assert
        Assert.Equal(3, state.FavoriteFilters.Count);

        // Act - Add new recent
        var updatedRecent = state.RecentFilters.Enqueue(Constants.FilterSourceContainsTest);

        state = Reducers.ReduceAddRecentFilterCompleted(
            state,
            new AddRecentFilterCompletedAction(updatedRecent));

        // Assert
        Assert.Equal(2, state.RecentFilters.Count());

        // Act - Remove a favorite
        var finalFavorites = state.FavoriteFilters.Remove(Constants.FilterIdEquals100);
        var finalRecent = state.RecentFilters.Enqueue(Constants.FilterIdEquals100);

        state = Reducers.ReduceRemoveFavoriteFilterCompleted(
            state,
            new RemoveFavoriteFilterCompletedAction(finalFavorites, finalRecent));

        // Assert final state
        Assert.Equal(2, state.FavoriteFilters.Count);
        Assert.DoesNotContain(Constants.FilterIdEquals100, state.FavoriteFilters);
        Assert.Equal(3, state.RecentFilters.Count());
        Assert.Equal(Constants.FilterIdEquals100, state.RecentFilters.Last());
    }

    [Fact]
    public void IntegrationTest_LoadThenAddFilters()
    {
        // Arrange
        var state = new FilterCacheState();

        // Act - Load initial filters
        var initialFavorites = ImmutableList.Create(Constants.FilterIdEquals100);
        var initialRecent = ImmutableQueue.Create(Constants.FilterLevelEqualsError);

        state = Reducers.ReduceLoadFiltersCompleted(
            state,
            new LoadFiltersCompletedAction(initialFavorites, initialRecent));

        // Assert
        Assert.Single(state.FavoriteFilters);
        Assert.Single(state.RecentFilters);

        // Act - Add new favorite
        var updatedFavorites = state.FavoriteFilters.Add(Constants.FilterIdEquals200);

        state = Reducers.ReduceAddFavoriteFilterCompleted(
            state,
            new AddFavoriteFilterCompletedAction(updatedFavorites));

        // Assert
        Assert.Equal(2, state.FavoriteFilters.Count);
        Assert.Single(state.RecentFilters);
    }

    [Fact]
    public void IntegrationTest_RemoveFavoriteAndAddToRecent()
    {
        // Arrange
        var favorites = ImmutableList.Create(Constants.FilterIdEquals100, Constants.FilterIdEquals200);
        var recent = ImmutableQueue<string>.Empty;

        var state = new FilterCacheState
        {
            FavoriteFilters = favorites,
            RecentFilters = recent
        };

        // Act - Remove favorite
        var updatedFavorites = state.FavoriteFilters.Remove(Constants.FilterIdEquals100);
        var updatedRecent = state.RecentFilters.Enqueue(Constants.FilterIdEquals100);

        state = Reducers.ReduceRemoveFavoriteFilterCompleted(
            state,
            new RemoveFavoriteFilterCompletedAction(updatedFavorites, updatedRecent));

        // Assert
        Assert.Single(state.FavoriteFilters);
        Assert.Contains(Constants.FilterIdEquals200, state.FavoriteFilters);
        Assert.DoesNotContain(Constants.FilterIdEquals100, state.FavoriteFilters);
        Assert.Single(state.RecentFilters);
        Assert.Equal(Constants.FilterIdEquals100, state.RecentFilters.First());
    }

    [Fact]
    public void ReduceAddFavoriteFilterCompleted_ShouldNotAffectRecentFilters()
    {
        // Arrange
        var existingRecent = ImmutableQueue.Create(Constants.FilterLevelEqualsError);
        var state = new FilterCacheState { RecentFilters = existingRecent };
        var favorites = ImmutableList.Create(Constants.FilterIdEquals100);
        var action = new AddFavoriteFilterCompletedAction(favorites);

        // Act
        var newState = Reducers.ReduceAddFavoriteFilterCompleted(state, action);

        // Assert
        Assert.Single(newState.FavoriteFilters);
        Assert.Single(newState.RecentFilters);
        Assert.Equal(Constants.FilterLevelEqualsError, newState.RecentFilters.First());
    }

    [Fact]
    public void ReduceAddFavoriteFilterCompleted_ShouldUpdateFavorites()
    {
        // Arrange
        var state = new FilterCacheState();
        var filters = ImmutableList.Create(Constants.FilterIdEquals100, Constants.FilterIdEquals200);
        var action = new AddFavoriteFilterCompletedAction(filters);

        // Act
        var newState = Reducers.ReduceAddFavoriteFilterCompleted(state, action);

        // Assert
        Assert.Equal(2, newState.FavoriteFilters.Count);
        Assert.Contains(Constants.FilterIdEquals100, newState.FavoriteFilters);
        Assert.Contains(Constants.FilterIdEquals200, newState.FavoriteFilters);
    }

    [Fact]
    public void ReduceAddRecentFilterCompleted_ShouldNotAffectFavoriteFilters()
    {
        // Arrange
        var existingFavorites = ImmutableList.Create(Constants.FilterLevelEqualsError);
        var state = new FilterCacheState { FavoriteFilters = existingFavorites };
        var recent = ImmutableQueue.Create(Constants.FilterIdEquals100);
        var action = new AddRecentFilterCompletedAction(recent);

        // Act
        var newState = Reducers.ReduceAddRecentFilterCompleted(state, action);

        // Assert
        Assert.Single(newState.RecentFilters);
        Assert.Single(newState.FavoriteFilters);
        Assert.Equal(Constants.FilterLevelEqualsError, newState.FavoriteFilters.First());
    }

    [Fact]
    public void ReduceAddRecentFilterCompleted_ShouldUpdateRecent()
    {
        // Arrange
        var state = new FilterCacheState();
        var filters = ImmutableQueue.Create(Constants.FilterIdEquals100, Constants.FilterIdEquals200);
        var action = new AddRecentFilterCompletedAction(filters);

        // Act
        var newState = Reducers.ReduceAddRecentFilterCompleted(state, action);

        // Assert
        Assert.Equal(2, newState.RecentFilters.Count());
    }

    [Fact]
    public void ReduceLoadFiltersCompleted_ShouldReplaceExistingFilters()
    {
        // Arrange
        var existingFavorites = ImmutableList.Create(Constants.FilterIdGreaterThan100);
        var existingRecent = ImmutableQueue.Create(Constants.FilterSourceContainsTest);

        var state = new FilterCacheState
        {
            FavoriteFilters = existingFavorites,
            RecentFilters = existingRecent
        };

        var newFavorites = ImmutableList.Create(Constants.FilterIdEquals100);
        var newRecent = ImmutableQueue.Create(Constants.FilterLevelEqualsError);
        var action = new LoadFiltersCompletedAction(newFavorites, newRecent);

        // Act
        var newState = Reducers.ReduceLoadFiltersCompleted(state, action);

        // Assert
        Assert.Single(newState.FavoriteFilters);
        Assert.Equal(Constants.FilterIdEquals100, newState.FavoriteFilters.First());
        Assert.Single(newState.RecentFilters);
        Assert.Equal(Constants.FilterLevelEqualsError, newState.RecentFilters.First());
    }

    [Fact]
    public void ReduceLoadFiltersCompleted_ShouldUpdateBothFilters()
    {
        // Arrange
        var state = new FilterCacheState();
        var favorites = ImmutableList.Create(Constants.FilterIdEquals100, Constants.FilterIdEquals200);
        var recent = ImmutableQueue.Create(Constants.FilterLevelEqualsError);
        var action = new LoadFiltersCompletedAction(favorites, recent);

        // Act
        var newState = Reducers.ReduceLoadFiltersCompleted(state, action);

        // Assert
        Assert.Equal(2, newState.FavoriteFilters.Count);
        Assert.Single(newState.RecentFilters);
    }

    [Fact]
    public void ReduceRemoveFavoriteFilterCompleted_ShouldUpdateBothFilters()
    {
        // Arrange
        var favorites = ImmutableList.Create(Constants.FilterIdEquals100, Constants.FilterIdEquals200);
        var recent = ImmutableQueue.Create(Constants.FilterLevelEqualsError);

        var state = new FilterCacheState
        {
            FavoriteFilters = favorites,
            RecentFilters = recent
        };

        var updatedFavorites = ImmutableList.Create(Constants.FilterIdEquals200);
        var updatedRecent = ImmutableQueue.Create(Constants.FilterLevelEqualsError, Constants.FilterIdEquals100);
        var action = new RemoveFavoriteFilterCompletedAction(updatedFavorites, updatedRecent);

        // Act
        var newState = Reducers.ReduceRemoveFavoriteFilterCompleted(state, action);

        // Assert
        Assert.Single(newState.FavoriteFilters);
        Assert.Contains(Constants.FilterIdEquals200, newState.FavoriteFilters);
        Assert.Equal(2, newState.RecentFilters.Count());
    }

    [Fact]
    public void ReduceRemoveFavoriteFilterCompleted_WithEmptyFavorites_ShouldClearFavorites()
    {
        // Arrange
        var favorites = ImmutableList.Create(Constants.FilterIdEquals100);
        var state = new FilterCacheState { FavoriteFilters = favorites };

        var action = new RemoveFavoriteFilterCompletedAction(
            ImmutableList<string>.Empty,
            ImmutableQueue.Create(Constants.FilterIdEquals100));

        // Act
        var newState = Reducers.ReduceRemoveFavoriteFilterCompleted(state, action);

        // Assert
        Assert.Empty(newState.FavoriteFilters);
        Assert.Single(newState.RecentFilters);
    }
}
