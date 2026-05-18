// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.FilterCache;
using EventLogExpert.Runtime.Tests.TestUtils.Constants;
using EventLogExpert.Filtering.TestUtils.Constants;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Tests.FilterCache;

public sealed class FilterCacheStoreTests
{
    [Fact]
    public void FilterCacheAction_AddFavoriteFilter_ShouldStoreFilter()
    {
        // Arrange
        var filter = FilterTestConstants.FilterIdEquals100;

        // Act
        var action = new AddFavoriteFilterAction(filter);

        // Assert
        Assert.Equal(filter, action.Filter);
    }

    [Fact]
    public void FilterCacheAction_AddFavoriteFilterSuccess_ShouldStoreFilters()
    {
        // Arrange
        var filters = ImmutableList.Create(FilterTestConstants.FilterIdEquals100, FilterTestConstants.FilterIdEquals200);

        // Act
        var action = new AddFavoriteFilterSuccessAction(filters);

        // Assert
        Assert.Equal(2, action.Filters.Count);
        Assert.Contains(FilterTestConstants.FilterIdEquals100, action.Filters);
        Assert.Contains(FilterTestConstants.FilterIdEquals200, action.Filters);
    }

    [Fact]
    public void FilterCacheAction_AddRecentFilter_ShouldStoreFilter()
    {
        // Arrange
        var filter = FilterTestConstants.FilterLevelEqualsError;

        // Act
        var action = new AddRecentFilterAction(filter);

        // Assert
        Assert.Equal(filter, action.Filter);
    }

    [Fact]
    public void FilterCacheAction_AddRecentFilterSuccess_ShouldStoreFilters()
    {
        // Arrange
        var filters = ImmutableQueue.Create(FilterTestConstants.FilterIdEquals100, FilterTestConstants.FilterIdEquals200);

        // Act
        var action = new AddRecentFilterSuccessAction(filters);

        // Assert
        Assert.Equal(2, action.Filters.Count());
    }

    [Fact]
    public void FilterCacheAction_ImportFavorites_ShouldStoreFilters()
    {
        // Arrange
        var filters = new List<string> { FilterTestConstants.FilterIdEquals100, FilterTestConstants.FilterIdEquals200 };

        // Act
        var action = new ImportFavoritesAction([.. filters]);

        // Assert
        Assert.Equal(2, action.Filters.Count);
    }

    [Fact]
    public void FilterCacheAction_LoadFiltersSuccess_ShouldStoreBothFilters()
    {
        // Arrange
        var favorites = ImmutableList.Create(FilterTestConstants.FilterIdEquals100);
        var recent = ImmutableQueue.Create(FilterTestConstants.FilterIdEquals200);

        // Act
        var action = new LoadFiltersSuccessAction(favorites, recent);

        // Assert
        Assert.Single(action.FavoriteFilters);
        Assert.Single(action.RecentFilters);
    }

    [Fact]
    public void FilterCacheAction_RemoveFavoriteFilter_ShouldStoreFilter()
    {
        // Arrange
        var filter = FilterTestConstants.FilterIdEquals100;

        // Act
        var action = new RemoveFavoriteFilterAction(filter);

        // Assert
        Assert.Equal(filter, action.Filter);
    }

    [Fact]
    public void FilterCacheAction_RemoveFavoriteFilterSuccess_ShouldStoreBothFilters()
    {
        // Arrange
        var favorites = ImmutableList.Create(FilterTestConstants.FilterIdEquals100);
        var recent = ImmutableQueue.Create(FilterTestConstants.FilterIdEquals200);

        // Act
        var action = new RemoveFavoriteFilterSuccessAction(favorites, recent);

        // Assert
        Assert.Single(action.FavoriteFilters);
        Assert.Single(action.RecentFilters);
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
        var favorites1 = ImmutableList.Create(FilterTestConstants.FilterIdEquals100);

        state = Reducers.ReduceAddFavoriteFilterSuccess(
            state,
            new AddFavoriteFilterSuccessAction(favorites1));

        // Assert
        Assert.Single(state.FavoriteFilters);

        // Act - Add second favorite
        var favorites2 = favorites1.Add(FilterTestConstants.FilterIdEquals200);

        state = Reducers.ReduceAddFavoriteFilterSuccess(
            state,
            new AddFavoriteFilterSuccessAction(favorites2));

        // Assert
        Assert.Equal(2, state.FavoriteFilters.Count);
        Assert.Contains(FilterTestConstants.FilterIdEquals100, state.FavoriteFilters);
        Assert.Contains(FilterTestConstants.FilterIdEquals200, state.FavoriteFilters);
    }

    [Fact]
    public void IntegrationTest_AddMultipleRecent()
    {
        // Arrange
        var state = new FilterCacheState();

        // Act - Add first recent
        var recent1 = ImmutableQueue.Create(FilterTestConstants.FilterIdEquals100);

        state = Reducers.ReduceAddRecentFilterSuccess(
            state,
            new AddRecentFilterSuccessAction(recent1));

        // Assert
        Assert.Single(state.RecentFilters);

        // Act - Add second recent
        var recent2 = recent1.Enqueue(FilterTestConstants.FilterIdEquals200);

        state = Reducers.ReduceAddRecentFilterSuccess(
            state,
            new AddRecentFilterSuccessAction(recent2));

        // Assert
        Assert.Equal(2, state.RecentFilters.Count());
        Assert.Equal(FilterTestConstants.FilterIdEquals100, state.RecentFilters.First());
    }

    [Fact]
    public void IntegrationTest_ComplexFilterManagement()
    {
        // Arrange
        var state = new FilterCacheState();

        // Act - Load initial state
        var initialFavorites = ImmutableList.Create(
            FilterTestConstants.FilterIdEquals100,
            FilterTestConstants.FilterIdEquals200);

        var initialRecent = ImmutableQueue.Create(FilterTestConstants.FilterLevelEqualsError);

        state = Reducers.ReduceLoadFiltersSuccess(
            state,
            new LoadFiltersSuccessAction(initialFavorites, initialRecent));

        // Assert initial state
        Assert.Equal(2, state.FavoriteFilters.Count);
        Assert.Single(state.RecentFilters);

        // Act - Add new favorite
        var updatedFavorites = state.FavoriteFilters.Add(FilterTestConstants.FilterIdGreaterThan100);

        state = Reducers.ReduceAddFavoriteFilterSuccess(
            state,
            new AddFavoriteFilterSuccessAction(updatedFavorites));

        // Assert
        Assert.Equal(3, state.FavoriteFilters.Count);

        // Act - Add new recent
        var updatedRecent = state.RecentFilters.Enqueue(FilterTestConstants.FilterSourceContainsTest);

        state = Reducers.ReduceAddRecentFilterSuccess(
            state,
            new AddRecentFilterSuccessAction(updatedRecent));

        // Assert
        Assert.Equal(2, state.RecentFilters.Count());

        // Act - Remove a favorite
        var finalFavorites = state.FavoriteFilters.Remove(FilterTestConstants.FilterIdEquals100);
        var finalRecent = state.RecentFilters.Enqueue(FilterTestConstants.FilterIdEquals100);

        state = Reducers.ReduceRemoveFavoriteFilterSuccess(
            state,
            new RemoveFavoriteFilterSuccessAction(finalFavorites, finalRecent));

        // Assert final state
        Assert.Equal(2, state.FavoriteFilters.Count);
        Assert.DoesNotContain(FilterTestConstants.FilterIdEquals100, state.FavoriteFilters);
        Assert.Equal(3, state.RecentFilters.Count());
        Assert.Equal(FilterTestConstants.FilterIdEquals100, state.RecentFilters.Last());
    }

    [Fact]
    public void IntegrationTest_LoadThenAddFilters()
    {
        // Arrange
        var state = new FilterCacheState();

        // Act - Load initial filters
        var initialFavorites = ImmutableList.Create(FilterTestConstants.FilterIdEquals100);
        var initialRecent = ImmutableQueue.Create(FilterTestConstants.FilterLevelEqualsError);

        state = Reducers.ReduceLoadFiltersSuccess(
            state,
            new LoadFiltersSuccessAction(initialFavorites, initialRecent));

        // Assert
        Assert.Single(state.FavoriteFilters);
        Assert.Single(state.RecentFilters);

        // Act - Add new favorite
        var updatedFavorites = state.FavoriteFilters.Add(FilterTestConstants.FilterIdEquals200);

        state = Reducers.ReduceAddFavoriteFilterSuccess(
            state,
            new AddFavoriteFilterSuccessAction(updatedFavorites));

        // Assert
        Assert.Equal(2, state.FavoriteFilters.Count);
        Assert.Single(state.RecentFilters);
    }

    [Fact]
    public void IntegrationTest_RemoveFavoriteAndAddToRecent()
    {
        // Arrange
        var favorites = ImmutableList.Create(FilterTestConstants.FilterIdEquals100, FilterTestConstants.FilterIdEquals200);
        var recent = ImmutableQueue<string>.Empty;

        var state = new FilterCacheState
        {
            FavoriteFilters = favorites,
            RecentFilters = recent
        };

        // Act - Remove favorite
        var updatedFavorites = state.FavoriteFilters.Remove(FilterTestConstants.FilterIdEquals100);
        var updatedRecent = state.RecentFilters.Enqueue(FilterTestConstants.FilterIdEquals100);

        state = Reducers.ReduceRemoveFavoriteFilterSuccess(
            state,
            new RemoveFavoriteFilterSuccessAction(updatedFavorites, updatedRecent));

        // Assert
        Assert.Single(state.FavoriteFilters);
        Assert.Contains(FilterTestConstants.FilterIdEquals200, state.FavoriteFilters);
        Assert.DoesNotContain(FilterTestConstants.FilterIdEquals100, state.FavoriteFilters);
        Assert.Single(state.RecentFilters);
        Assert.Equal(FilterTestConstants.FilterIdEquals100, state.RecentFilters.First());
    }

    [Fact]
    public void ReduceAddFavoriteFilterSuccess_ShouldNotAffectRecentFilters()
    {
        // Arrange
        var existingRecent = ImmutableQueue.Create(FilterTestConstants.FilterLevelEqualsError);
        var state = new FilterCacheState { RecentFilters = existingRecent };
        var favorites = ImmutableList.Create(FilterTestConstants.FilterIdEquals100);
        var action = new AddFavoriteFilterSuccessAction(favorites);

        // Act
        var newState = Reducers.ReduceAddFavoriteFilterSuccess(state, action);

        // Assert
        Assert.Single(newState.FavoriteFilters);
        Assert.Single(newState.RecentFilters);
        Assert.Equal(FilterTestConstants.FilterLevelEqualsError, newState.RecentFilters.First());
    }

    [Fact]
    public void ReduceAddFavoriteFilterSuccess_ShouldUpdateFavorites()
    {
        // Arrange
        var state = new FilterCacheState();
        var filters = ImmutableList.Create(FilterTestConstants.FilterIdEquals100, FilterTestConstants.FilterIdEquals200);
        var action = new AddFavoriteFilterSuccessAction(filters);

        // Act
        var newState = Reducers.ReduceAddFavoriteFilterSuccess(state, action);

        // Assert
        Assert.Equal(2, newState.FavoriteFilters.Count);
        Assert.Contains(FilterTestConstants.FilterIdEquals100, newState.FavoriteFilters);
        Assert.Contains(FilterTestConstants.FilterIdEquals200, newState.FavoriteFilters);
    }

    [Fact]
    public void ReduceAddRecentFilterSuccess_ShouldNotAffectFavoriteFilters()
    {
        // Arrange
        var existingFavorites = ImmutableList.Create(FilterTestConstants.FilterLevelEqualsError);
        var state = new FilterCacheState { FavoriteFilters = existingFavorites };
        var recent = ImmutableQueue.Create(FilterTestConstants.FilterIdEquals100);
        var action = new AddRecentFilterSuccessAction(recent);

        // Act
        var newState = Reducers.ReduceAddRecentFilterSuccess(state, action);

        // Assert
        Assert.Single(newState.RecentFilters);
        Assert.Single(newState.FavoriteFilters);
        Assert.Equal(FilterTestConstants.FilterLevelEqualsError, newState.FavoriteFilters.First());
    }

    [Fact]
    public void ReduceAddRecentFilterSuccess_ShouldUpdateRecent()
    {
        // Arrange
        var state = new FilterCacheState();
        var filters = ImmutableQueue.Create(FilterTestConstants.FilterIdEquals100, FilterTestConstants.FilterIdEquals200);
        var action = new AddRecentFilterSuccessAction(filters);

        // Act
        var newState = Reducers.ReduceAddRecentFilterSuccess(state, action);

        // Assert
        Assert.Equal(2, newState.RecentFilters.Count());
    }

    [Fact]
    public void ReduceLoadFiltersSuccess_ShouldReplaceExistingFilters()
    {
        // Arrange
        var existingFavorites = ImmutableList.Create(FilterTestConstants.FilterIdGreaterThan100);
        var existingRecent = ImmutableQueue.Create(FilterTestConstants.FilterSourceContainsTest);

        var state = new FilterCacheState
        {
            FavoriteFilters = existingFavorites,
            RecentFilters = existingRecent
        };

        var newFavorites = ImmutableList.Create(FilterTestConstants.FilterIdEquals100);
        var newRecent = ImmutableQueue.Create(FilterTestConstants.FilterLevelEqualsError);
        var action = new LoadFiltersSuccessAction(newFavorites, newRecent);

        // Act
        var newState = Reducers.ReduceLoadFiltersSuccess(state, action);

        // Assert
        Assert.Single(newState.FavoriteFilters);
        Assert.Equal(FilterTestConstants.FilterIdEquals100, newState.FavoriteFilters.First());
        Assert.Single(newState.RecentFilters);
        Assert.Equal(FilterTestConstants.FilterLevelEqualsError, newState.RecentFilters.First());
    }

    [Fact]
    public void ReduceLoadFiltersSuccess_ShouldUpdateBothFilters()
    {
        // Arrange
        var state = new FilterCacheState();
        var favorites = ImmutableList.Create(FilterTestConstants.FilterIdEquals100, FilterTestConstants.FilterIdEquals200);
        var recent = ImmutableQueue.Create(FilterTestConstants.FilterLevelEqualsError);
        var action = new LoadFiltersSuccessAction(favorites, recent);

        // Act
        var newState = Reducers.ReduceLoadFiltersSuccess(state, action);

        // Assert
        Assert.Equal(2, newState.FavoriteFilters.Count);
        Assert.Single(newState.RecentFilters);
    }

    [Fact]
    public void ReduceRemoveFavoriteFilterSuccess_ShouldUpdateBothFilters()
    {
        // Arrange
        var favorites = ImmutableList.Create(FilterTestConstants.FilterIdEquals100, FilterTestConstants.FilterIdEquals200);
        var recent = ImmutableQueue.Create(FilterTestConstants.FilterLevelEqualsError);

        var state = new FilterCacheState
        {
            FavoriteFilters = favorites,
            RecentFilters = recent
        };

        var updatedFavorites = ImmutableList.Create(FilterTestConstants.FilterIdEquals200);
        var updatedRecent = ImmutableQueue.Create(FilterTestConstants.FilterLevelEqualsError, FilterTestConstants.FilterIdEquals100);
        var action = new RemoveFavoriteFilterSuccessAction(updatedFavorites, updatedRecent);

        // Act
        var newState = Reducers.ReduceRemoveFavoriteFilterSuccess(state, action);

        // Assert
        Assert.Single(newState.FavoriteFilters);
        Assert.Contains(FilterTestConstants.FilterIdEquals200, newState.FavoriteFilters);
        Assert.Equal(2, newState.RecentFilters.Count());
    }

    [Fact]
    public void ReduceRemoveFavoriteFilterSuccess_WithEmptyFavorites_ShouldClearFavorites()
    {
        // Arrange
        var favorites = ImmutableList.Create(FilterTestConstants.FilterIdEquals100);
        var state = new FilterCacheState { FavoriteFilters = favorites };

        var action = new RemoveFavoriteFilterSuccessAction(
            [],
            [FilterTestConstants.FilterIdEquals100]);

        // Act
        var newState = Reducers.ReduceRemoveFavoriteFilterSuccess(state, action);

        // Assert
        Assert.Empty(newState.FavoriteFilters);
        Assert.Single(newState.RecentFilters);
    }
}
