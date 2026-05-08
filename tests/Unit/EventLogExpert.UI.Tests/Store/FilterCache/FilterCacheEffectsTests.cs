// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Store.FilterCache;
using EventLogExpert.UI.Tests.TestUtils.Constants;
using Fluxor;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Tests.Store.FilterCache;

public sealed class FilterCacheEffectsTests
{
    [Fact]
    public async Task HandleAddFavoriteFilter_ShouldPersistToPreferences()
    {
        // Arrange
        var existingFavorites = ImmutableList.Create(Constants.FilterIdEquals100);

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(
            existingFavorites);

        var action = new FilterCacheAction.AddFavoriteFilter(Constants.FilterLevelEqualsError);

        // Act
        await effects.HandleAddFavoriteFilter(action, mockDispatcher);

        // Assert
        var _ = mockPreferencesProvider.Received(1).FavoriteFiltersPreference = Arg.Is<IEnumerable<string>>(x =>
            x.Contains(Constants.FilterIdEquals100) &&
            x.Contains(Constants.FilterLevelEqualsError) &&
            x.Count() == 2);
    }

    [Fact]
    public async Task HandleAddFavoriteFilter_WhenFilterAlreadyExists_ShouldNotDispatch()
    {
        // Arrange
        var existingFavorites = ImmutableList.Create(Constants.FilterIdEquals100);

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(
            existingFavorites);

        var action = new FilterCacheAction.AddFavoriteFilter(Constants.FilterIdEquals100);

        // Act
        await effects.HandleAddFavoriteFilter(action, mockDispatcher);

        // Assert
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<FilterCacheAction.AddFavoriteFilterCompleted>());
    }

    [Fact]
    public async Task HandleAddFavoriteFilter_WhenFilterDoesNotExist_ShouldAddToFavorites()
    {
        // Arrange
        var existingFavorites = ImmutableList.Create(Constants.FilterIdEquals100);

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(
            existingFavorites);

        var action = new FilterCacheAction.AddFavoriteFilter(Constants.FilterIdEquals200);

        // Act
        await effects.HandleAddFavoriteFilter(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<FilterCacheAction.AddFavoriteFilterCompleted>(x =>
            x.Filters.Count == 2 &&
            x.Filters.Contains(Constants.FilterIdEquals100) &&
            x.Filters.Contains(Constants.FilterIdEquals200)));
    }

    [Fact]
    public async Task HandleAddRecentFilter_ShouldPersistToPreferences()
    {
        // Arrange
        var existingRecent = ImmutableQueue.Create(Constants.FilterIdEquals100);

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(
            recentFilters: existingRecent);

        var action = new FilterCacheAction.AddRecentFilter(Constants.FilterLevelEqualsError);

        // Act
        await effects.HandleAddRecentFilter(action, mockDispatcher);

        // Assert
        var _ = mockPreferencesProvider.Received(1).RecentFiltersPreference = Arg.Is<IEnumerable<string>>(x =>
            x.Contains(Constants.FilterIdEquals100) &&
            x.Contains(Constants.FilterLevelEqualsError) &&
            x.Count() == 2);
    }

    [Fact]
    public async Task HandleAddRecentFilter_WhenFilterAlreadyExists_ShouldNotDispatch()
    {
        // Arrange
        var existingRecent = ImmutableQueue.Create(Constants.FilterIdEquals100);

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(
            recentFilters: existingRecent);

        var action = new FilterCacheAction.AddRecentFilter(Constants.FilterIdEquals100);

        // Act
        await effects.HandleAddRecentFilter(action, mockDispatcher);

        // Assert
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<FilterCacheAction.AddRecentFilterCompleted>());
    }

    [Fact]
    public async Task HandleAddRecentFilter_WhenFilterExistsCaseInsensitive_ShouldNotDispatch()
    {
        // Arrange
        var existingRecent = ImmutableQueue.Create("Id == 100");

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(
            recentFilters: existingRecent);

        var action = new FilterCacheAction.AddRecentFilter("ID == 100");

        // Act
        await effects.HandleAddRecentFilter(action, mockDispatcher);

        // Assert
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<FilterCacheAction.AddRecentFilterCompleted>());
    }

    [Fact]
    public async Task HandleAddRecentFilter_WhenFilterIsEmpty_ShouldNotDispatch()
    {
        // Arrange
        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects();

        var action = new FilterCacheAction.AddRecentFilter("");

        // Act
        await effects.HandleAddRecentFilter(action, mockDispatcher);

        // Assert
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<FilterCacheAction.AddRecentFilterCompleted>());
    }

    [Fact]
    public async Task HandleAddRecentFilter_WhenFilterIsNew_ShouldAddToRecent()
    {
        // Arrange
        var existingRecent = ImmutableQueue.Create(Constants.FilterIdEquals100);

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(
            recentFilters: existingRecent);

        var action = new FilterCacheAction.AddRecentFilter(Constants.FilterLevelEqualsError);

        // Act
        await effects.HandleAddRecentFilter(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<FilterCacheAction.AddRecentFilterCompleted>(x =>
            x.Filters.Count() == 2));
    }

    [Fact]
    public async Task HandleAddRecentFilter_WhenFilterIsWhitespace_ShouldNotDispatch()
    {
        // Arrange
        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects();

        var action = new FilterCacheAction.AddRecentFilter("   ");

        // Act
        await effects.HandleAddRecentFilter(action, mockDispatcher);

        // Assert
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<FilterCacheAction.AddRecentFilterCompleted>());
    }

    [Fact]
    public async Task HandleAddRecentFilter_WhenMaxReached_ShouldDequeueOldest()
    {
        // Arrange
        var filters = Enumerable.Range(1, 20)
            .Select(i => $"Filter{i}")
            .ToList();

        var existingRecent = ImmutableQueue.CreateRange(filters);

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(
            recentFilters: existingRecent);

        var action = new FilterCacheAction.AddRecentFilter("NewFilter");

        // Act
        await effects.HandleAddRecentFilter(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<FilterCacheAction.AddRecentFilterCompleted>(a =>
            a.Filters.Count() == 20 &&
            !a.Filters.Contains("Filter1") &&
            a.Filters.Contains("NewFilter")));
    }

    [Fact]
    public async Task HandleImportFavorites_ShouldBeCaseInsensitive()
    {
        // Arrange
        var existingFavorites = ImmutableList.Create("Id == 100");

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(
            existingFavorites);

        var filtersToImport = new List<string>
        {
            "ID == 100",
            Constants.FilterLevelEqualsError
        };

        var action = new FilterCacheAction.ImportFavorites(filtersToImport);

        // Act
        await effects.HandleImportFavorites(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<FilterCacheAction.AddFavoriteFilterCompleted>(a =>
            a.Filters.Count == 2 &&
            a.Filters.Contains(Constants.FilterLevelEqualsError)));
    }

    [Fact]
    public async Task HandleImportFavorites_ShouldPersistToPreferences()
    {
        // Arrange
        var existingFavorites = ImmutableList.Create(Constants.FilterIdEquals100);

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(
            existingFavorites);

        var filtersToImport = new List<string> { Constants.FilterLevelEqualsError };
        var action = new FilterCacheAction.ImportFavorites(filtersToImport);

        // Act
        await effects.HandleImportFavorites(action, mockDispatcher);

        // Assert
        var _ = mockPreferencesProvider.Received(1).FavoriteFiltersPreference = Arg.Is<IEnumerable<string>>(x =>
            x.Contains(Constants.FilterIdEquals100) &&
            x.Contains(Constants.FilterLevelEqualsError) &&
            x.Count() == 2);
    }

    [Fact]
    public async Task HandleImportFavorites_WhenFiltersAlreadyExist_ShouldNotAddDuplicates()
    {
        // Arrange
        var existingFavorites = ImmutableList.Create(
            Constants.FilterIdEquals100,
            Constants.FilterLevelEqualsError);

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(
            existingFavorites);

        var filtersToImport = new List<string>
        {
            Constants.FilterIdEquals100,
            Constants.FilterLevelEqualsError,
            Constants.FilterSourceContainsTest
        };

        var action = new FilterCacheAction.ImportFavorites(filtersToImport);

        // Act
        await effects.HandleImportFavorites(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<FilterCacheAction.AddFavoriteFilterCompleted>(x =>
            x.Filters.Count == 3 &&
            x.Filters.Contains(Constants.FilterSourceContainsTest)));
    }

    [Fact]
    public async Task HandleImportFavorites_WhenFiltersAreNew_ShouldAddAll()
    {
        // Arrange
        var existingFavorites = ImmutableList.Create(Constants.FilterIdEquals100);

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(
            existingFavorites);

        var filtersToImport = new List<string>
        {
            Constants.FilterLevelEqualsError,
            Constants.FilterSourceContainsTest
        };

        var action = new FilterCacheAction.ImportFavorites(filtersToImport);

        // Act
        await effects.HandleImportFavorites(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<FilterCacheAction.AddFavoriteFilterCompleted>(x =>
            x.Filters.Count == 3 &&
            x.Filters.Contains(Constants.FilterIdEquals100) &&
            x.Filters.Contains(Constants.FilterLevelEqualsError) &&
            x.Filters.Contains(Constants.FilterSourceContainsTest)));
    }

    [Fact]
    public async Task HandleImportFavorites_WhenImportContainsCaseInsensitiveDuplicates_ShouldDedupe()
    {
        // Arrange
        var existingFavorites = ImmutableList<string>.Empty;

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(
            existingFavorites);

        var filtersToImport = new List<string>
        {
            "Id == 100",
            "ID == 100",
            Constants.FilterLevelEqualsError
        };

        var action = new FilterCacheAction.ImportFavorites(filtersToImport);

        // Act
        await effects.HandleImportFavorites(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<FilterCacheAction.AddFavoriteFilterCompleted>(a =>
            a.Filters.Count == 2 &&
            a.Filters.Contains("Id == 100") &&
            a.Filters.Contains(Constants.FilterLevelEqualsError)));
    }

    [Fact]
    public async Task HandleLoadFilters_ShouldLoadBothFavoritesAndRecent()
    {
        // Arrange
        var favoritesPreference = new List<string>
        {
            Constants.FilterIdEquals100,
            Constants.FilterLevelEqualsError
        };

        var recentPreference = new List<string>
        {
            Constants.FilterSourceContainsTest
        };

        var mockPreferencesProvider = Substitute.For<IPreferencesProvider>();
        mockPreferencesProvider.FavoriteFiltersPreference.Returns(favoritesPreference);
        mockPreferencesProvider.RecentFiltersPreference.Returns(recentPreference);

        var mockState = Substitute.For<IState<FilterCacheState>>();
        mockState.Value.Returns(new FilterCacheState());

        var effects = new FilterCacheEffects(mockPreferencesProvider, mockState);
        var mockDispatcher = Substitute.For<IDispatcher>();
        var action = new FilterCacheAction.LoadFilters();

        // Act
        await effects.HandleLoadFilters(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<FilterCacheAction.LoadFiltersCompleted>(x =>
            x.FavoriteFilters.Count == 2 &&
            x.RecentFilters.Count() == 1 &&
            x.FavoriteFilters.Contains(Constants.FilterIdEquals100) &&
            x.FavoriteFilters.Contains(Constants.FilterLevelEqualsError) &&
            x.RecentFilters.Contains(Constants.FilterSourceContainsTest)));
    }

    [Fact]
    public async Task HandleLoadFilters_WhenPreferencesEmpty_ShouldLoadEmptyLists()
    {
        // Arrange
        var mockPreferencesProvider = Substitute.For<IPreferencesProvider>();
        mockPreferencesProvider.FavoriteFiltersPreference.Returns(new List<string>());
        mockPreferencesProvider.RecentFiltersPreference.Returns(new List<string>());

        var mockState = Substitute.For<IState<FilterCacheState>>();
        mockState.Value.Returns(new FilterCacheState());

        var effects = new FilterCacheEffects(mockPreferencesProvider, mockState);
        var mockDispatcher = Substitute.For<IDispatcher>();
        var action = new FilterCacheAction.LoadFilters();

        // Act
        await effects.HandleLoadFilters(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<FilterCacheAction.LoadFiltersCompleted>(x =>
            x.FavoriteFilters.Count == 0 &&
            !x.RecentFilters.Any()));
    }

    [Fact]
    public async Task HandleRemoveFavoriteFilter_ShouldPersistBothPreferences()
    {
        // Arrange
        var existingFavorites = ImmutableList.Create(
            Constants.FilterIdEquals100,
            Constants.FilterLevelEqualsError);

        var existingRecent = ImmutableQueue.Create(Constants.FilterSourceContainsTest);

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(
            existingFavorites,
            existingRecent);

        var action = new FilterCacheAction.RemoveFavoriteFilter(Constants.FilterIdEquals100);

        // Act
        await effects.HandleRemoveFavoriteFilter(action, mockDispatcher);

        // Assert
        _ = mockPreferencesProvider.Received(1).FavoriteFiltersPreference = Arg.Is<IEnumerable<string>>(x =>
            !x.Contains(Constants.FilterIdEquals100) &&
            x.Contains(Constants.FilterLevelEqualsError));

        _ = mockPreferencesProvider.Received(1).RecentFiltersPreference = Arg.Is<IEnumerable<string>>(x =>
            x.Contains(Constants.FilterIdEquals100));
    }

    [Fact]
    public async Task HandleRemoveFavoriteFilter_WhenFilterInRecent_ShouldRemoveFromFavoritesOnly()
    {
        // Arrange
        var existingFavorites = ImmutableList.Create(
            Constants.FilterIdEquals100,
            Constants.FilterLevelEqualsError);

        var existingRecent = ImmutableQueue.Create(Constants.FilterIdEquals100);

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(
            existingFavorites,
            existingRecent);

        var action = new FilterCacheAction.RemoveFavoriteFilter(Constants.FilterIdEquals100);

        // Act
        await effects.HandleRemoveFavoriteFilter(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<FilterCacheAction.RemoveFavoriteFilterCompleted>(x =>
            x.FavoriteFilters.Count == 1 &&
            !x.FavoriteFilters.Contains(Constants.FilterIdEquals100) &&
            x.RecentFilters.Count() == 1 &&
            x.RecentFilters.Contains(Constants.FilterIdEquals100)));
    }

    [Fact]
    public async Task HandleRemoveFavoriteFilter_WhenFilterNotInFavorites_ShouldNotDispatch()
    {
        // Arrange
        var existingFavorites = ImmutableList.Create(Constants.FilterIdEquals100);

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(
            existingFavorites);

        var action = new FilterCacheAction.RemoveFavoriteFilter(Constants.FilterLevelEqualsError);

        // Act
        await effects.HandleRemoveFavoriteFilter(action, mockDispatcher);

        // Assert
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<FilterCacheAction.RemoveFavoriteFilterCompleted>());
    }

    [Fact]
    public async Task HandleRemoveFavoriteFilter_WhenFilterNotInRecent_ShouldAddToRecent()
    {
        // Arrange
        var existingFavorites = ImmutableList.Create(
            Constants.FilterIdEquals100,
            Constants.FilterLevelEqualsError);

        var existingRecent = ImmutableQueue.Create(Constants.FilterSourceContainsTest);

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(
            existingFavorites,
            existingRecent);

        var action = new FilterCacheAction.RemoveFavoriteFilter(Constants.FilterIdEquals100);

        // Act
        await effects.HandleRemoveFavoriteFilter(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<FilterCacheAction.RemoveFavoriteFilterCompleted>(x =>
            x.FavoriteFilters.Count == 1 &&
            !x.FavoriteFilters.Contains(Constants.FilterIdEquals100) &&
            x.RecentFilters.Count() == 2 &&
            x.RecentFilters.Contains(Constants.FilterIdEquals100)));
    }

    [Fact]
    public async Task HandleRemoveFavoriteFilter_WhenRecentIsFull_ShouldDequeueOldest()
    {
        // Arrange
        var existingFavorites = ImmutableList.Create(Constants.FilterIdEquals100);

        var filters = Enumerable.Range(1, 20)
            .Select(i => $"Filter{i}")
            .ToList();

        var existingRecent = ImmutableQueue.CreateRange(filters);

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(
            existingFavorites,
            existingRecent);

        var action = new FilterCacheAction.RemoveFavoriteFilter(Constants.FilterIdEquals100);

        // Act
        await effects.HandleRemoveFavoriteFilter(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<FilterCacheAction.RemoveFavoriteFilterCompleted>(x =>
            x.FavoriteFilters.Count == 0 &&
            x.RecentFilters.Count() == 20 &&
            !x.RecentFilters.Contains("Filter1") &&
            x.RecentFilters.Contains(Constants.FilterIdEquals100)));
    }

    private static (FilterCacheEffects effects, IDispatcher mockDispatcher, IPreferencesProvider mockPreferencesProvider) CreateEffects(
        ImmutableList<string>? favoriteFilters = null,
        ImmutableQueue<string>? recentFilters = null)
    {
        var mockState = Substitute.For<IState<FilterCacheState>>();

        mockState.Value.Returns(new FilterCacheState
        {
            FavoriteFilters = favoriteFilters ?? ImmutableList<string>.Empty,
            RecentFilters = recentFilters ?? ImmutableQueue<string>.Empty
        });

        var mockPreferencesProvider = Substitute.For<IPreferencesProvider>();

        var effects = new FilterCacheEffects(mockPreferencesProvider, mockState);
        var mockDispatcher = Substitute.For<IDispatcher>();

        return (effects, mockDispatcher, mockPreferencesProvider);
    }
}
