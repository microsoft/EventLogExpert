// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.FilterCache;
using EventLogExpert.Runtime.Tests.TestUtils.Constants;
using EventLogExpert.Filtering.TestUtils.Constants;
using Fluxor;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Tests.FilterCache;

public sealed class EffectsTests
{
    [Fact]
    public async Task HandleAddFavoriteFilter_ShouldPersistToPreferences()
    {
        // Arrange
        var existingFavorites = ImmutableList.Create(FilterTestConstants.FilterIdEquals100);

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(
            existingFavorites);

        var action = new AddFavoriteFilterAction(FilterTestConstants.FilterLevelEqualsError);

        // Act
        await effects.HandleAddFavoriteFilter(action, mockDispatcher);

        // Assert
        var _ = mockPreferencesProvider.Received(1).FavoriteFiltersPreference = Arg.Is<IEnumerable<string>>(x =>
            x.Contains(FilterTestConstants.FilterIdEquals100) &&
            x.Contains(FilterTestConstants.FilterLevelEqualsError) &&
            x.Count() == 2);
    }

    [Fact]
    public async Task HandleAddFavoriteFilter_WhenFilterAlreadyExists_ShouldNotDispatch()
    {
        // Arrange
        var existingFavorites = ImmutableList.Create(FilterTestConstants.FilterIdEquals100);

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(
            existingFavorites);

        var action = new AddFavoriteFilterAction(FilterTestConstants.FilterIdEquals100);

        // Act
        await effects.HandleAddFavoriteFilter(action, mockDispatcher);

        // Assert
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<AddFavoriteFilterSuccessAction>());
    }

    [Fact]
    public async Task HandleAddFavoriteFilter_WhenFilterDoesNotExist_ShouldAddToFavorites()
    {
        // Arrange
        var existingFavorites = ImmutableList.Create(FilterTestConstants.FilterIdEquals100);

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(
            existingFavorites);

        var action = new AddFavoriteFilterAction(FilterTestConstants.FilterIdEquals200);

        // Act
        await effects.HandleAddFavoriteFilter(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<AddFavoriteFilterSuccessAction>(x =>
            x.Filters.Count == 2 &&
            x.Filters.Contains(FilterTestConstants.FilterIdEquals100) &&
            x.Filters.Contains(FilterTestConstants.FilterIdEquals200)));
    }

    [Fact]
    public async Task HandleAddFavoriteFilter_WhenFilterInRecent_ShouldRemoveFromRecent()
    {
        // Favorite and Recent are mutually exclusive in the picker; promoting to Favorite must drop
        // the same string from Recent so the cached-filter dropdown doesn't show it twice.
        var existingFavorites = ImmutableList<string>.Empty;
        var existingRecent = ImmutableQueue.Create(
            FilterTestConstants.FilterIdEquals100,
            FilterTestConstants.FilterLevelEqualsError);

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(
            existingFavorites,
            existingRecent);

        var action = new AddFavoriteFilterAction(FilterTestConstants.FilterIdEquals100);

        // Act
        await effects.HandleAddFavoriteFilter(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<AddFavoriteFilterSuccessAction>(x =>
            x.Filters.Count == 1 &&
            x.Filters.Contains(FilterTestConstants.FilterIdEquals100)));

        mockDispatcher.Received(1).Dispatch(Arg.Is<AddRecentFilterSuccessAction>(x =>
            x.Filters.Count() == 1 &&
            !x.Filters.Contains(FilterTestConstants.FilterIdEquals100) &&
            x.Filters.Contains(FilterTestConstants.FilterLevelEqualsError)));

        _ = mockPreferencesProvider.Received(1).RecentFiltersPreference = Arg.Is<IEnumerable<string>>(x =>
            !x.Contains(FilterTestConstants.FilterIdEquals100));
    }

    [Fact]
    public async Task HandleAddFavoriteFilter_WhenFilterNotInRecent_ShouldNotDispatchRecentAction()
    {
        // Arrange — recent untouched, so we shouldn't dispatch a no-op AddRecentFilterSuccessAction.
        var existingRecent = ImmutableQueue.Create(FilterTestConstants.FilterLevelEqualsError);

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(
            recentFilters: existingRecent);

        var action = new AddFavoriteFilterAction(FilterTestConstants.FilterIdEquals100);

        // Act
        await effects.HandleAddFavoriteFilter(action, mockDispatcher);

        // Assert
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<AddRecentFilterSuccessAction>());
    }

    [Fact]
    public async Task HandleAddRecentFilter_ShouldPersistToPreferences()
    {
        // Arrange
        var existingRecent = ImmutableQueue.Create(FilterTestConstants.FilterIdEquals100);

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(
            recentFilters: existingRecent);

        var action = new AddRecentFilterAction(FilterTestConstants.FilterLevelEqualsError);

        // Act
        await effects.HandleAddRecentFilter(action, mockDispatcher);

        // Assert
        var _ = mockPreferencesProvider.Received(1).RecentFiltersPreference = Arg.Is<IEnumerable<string>>(x =>
            x.Contains(FilterTestConstants.FilterIdEquals100) &&
            x.Contains(FilterTestConstants.FilterLevelEqualsError) &&
            x.Count() == 2);
    }

    [Fact]
    public async Task HandleAddRecentFilter_WhenFilterAlreadyExists_ShouldNotDispatch()
    {
        // Arrange
        var existingRecent = ImmutableQueue.Create(FilterTestConstants.FilterIdEquals100);

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(
            recentFilters: existingRecent);

        var action = new AddRecentFilterAction(FilterTestConstants.FilterIdEquals100);

        // Act
        await effects.HandleAddRecentFilter(action, mockDispatcher);

        // Assert
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<AddRecentFilterSuccessAction>());
    }

    [Fact]
    public async Task HandleAddRecentFilter_WhenFilterAlreadyFavorite_ShouldNotDispatch()
    {
        // Symmetric to the Favorite-eviction rule: a filter already in Favorites shouldn't get
        // duplicated into Recent (e.g., when re-applying a favorite filter through the row).
        var existingFavorites = ImmutableList.Create(FilterTestConstants.FilterIdEquals100);

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(
            existingFavorites);

        var action = new AddRecentFilterAction(FilterTestConstants.FilterIdEquals100);

        // Act
        await effects.HandleAddRecentFilter(action, mockDispatcher);

        // Assert
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<AddRecentFilterSuccessAction>());
    }

    [Fact]
    public async Task HandleAddRecentFilter_WhenFilterAlreadyFavoriteCaseInsensitive_ShouldNotDispatch()
    {
        // Arrange
        var existingFavorites = ImmutableList.Create("Id == 100");

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(
            existingFavorites);

        var action = new AddRecentFilterAction("ID == 100");

        // Act
        await effects.HandleAddRecentFilter(action, mockDispatcher);

        // Assert
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<AddRecentFilterSuccessAction>());
    }

    [Fact]
    public async Task HandleAddRecentFilter_WhenFilterExistsCaseInsensitive_ShouldNotDispatch()
    {
        // Arrange
        var existingRecent = ImmutableQueue.Create("Id == 100");

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(
            recentFilters: existingRecent);

        var action = new AddRecentFilterAction("ID == 100");

        // Act
        await effects.HandleAddRecentFilter(action, mockDispatcher);

        // Assert
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<AddRecentFilterSuccessAction>());
    }

    [Fact]
    public async Task HandleAddRecentFilter_WhenFilterIsEmpty_ShouldNotDispatch()
    {
        // Arrange
        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects();

        var action = new AddRecentFilterAction("");

        // Act
        await effects.HandleAddRecentFilter(action, mockDispatcher);

        // Assert
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<AddRecentFilterSuccessAction>());
    }

    [Fact]
    public async Task HandleAddRecentFilter_WhenFilterIsNew_ShouldAddToRecent()
    {
        // Arrange
        var existingRecent = ImmutableQueue.Create(FilterTestConstants.FilterIdEquals100);

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(
            recentFilters: existingRecent);

        var action = new AddRecentFilterAction(FilterTestConstants.FilterLevelEqualsError);

        // Act
        await effects.HandleAddRecentFilter(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<AddRecentFilterSuccessAction>(x =>
            x.Filters.Count() == 2));
    }

    [Fact]
    public async Task HandleAddRecentFilter_WhenFilterIsWhitespace_ShouldNotDispatch()
    {
        // Arrange
        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects();

        var action = new AddRecentFilterAction("   ");

        // Act
        await effects.HandleAddRecentFilter(action, mockDispatcher);

        // Assert
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<AddRecentFilterSuccessAction>());
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

        var action = new AddRecentFilterAction("NewFilter");

        // Act
        await effects.HandleAddRecentFilter(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<AddRecentFilterSuccessAction>(a =>
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
            FilterTestConstants.FilterLevelEqualsError
        };

        var action = new ImportFavoritesAction([.. filtersToImport]);

        // Act
        await effects.HandleImportFavorites(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<AddFavoriteFilterSuccessAction>(a =>
            a.Filters.Count == 2 &&
            a.Filters.Contains(FilterTestConstants.FilterLevelEqualsError)));
    }

    [Fact]
    public async Task HandleImportFavorites_ShouldPersistToPreferences()
    {
        // Arrange
        var existingFavorites = ImmutableList.Create(FilterTestConstants.FilterIdEquals100);

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(
            existingFavorites);

        var filtersToImport = new List<string> { FilterTestConstants.FilterLevelEqualsError };
        var action = new ImportFavoritesAction([.. filtersToImport]);

        // Act
        await effects.HandleImportFavorites(action, mockDispatcher);

        // Assert
        var _ = mockPreferencesProvider.Received(1).FavoriteFiltersPreference = Arg.Is<IEnumerable<string>>(x =>
            x.Contains(FilterTestConstants.FilterIdEquals100) &&
            x.Contains(FilterTestConstants.FilterLevelEqualsError) &&
            x.Count() == 2);
    }

    [Fact]
    public async Task HandleImportFavorites_WhenFiltersAlreadyExist_ShouldNotAddDuplicates()
    {
        // Arrange
        var existingFavorites = ImmutableList.Create(
            FilterTestConstants.FilterIdEquals100,
            FilterTestConstants.FilterLevelEqualsError);

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(
            existingFavorites);

        var filtersToImport = new List<string>
        {
            FilterTestConstants.FilterIdEquals100,
            FilterTestConstants.FilterLevelEqualsError,
            FilterTestConstants.FilterSourceContainsTest
        };

        var action = new ImportFavoritesAction([.. filtersToImport]);

        // Act
        await effects.HandleImportFavorites(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<AddFavoriteFilterSuccessAction>(x =>
            x.Filters.Count == 3 &&
            x.Filters.Contains(FilterTestConstants.FilterSourceContainsTest)));
    }

    [Fact]
    public async Task HandleImportFavorites_WhenFiltersAreNew_ShouldAddAll()
    {
        // Arrange
        var existingFavorites = ImmutableList.Create(FilterTestConstants.FilterIdEquals100);

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(
            existingFavorites);

        var filtersToImport = new List<string>
        {
            FilterTestConstants.FilterLevelEqualsError,
            FilterTestConstants.FilterSourceContainsTest
        };

        var action = new ImportFavoritesAction([.. filtersToImport]);

        // Act
        await effects.HandleImportFavorites(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<AddFavoriteFilterSuccessAction>(x =>
            x.Filters.Count == 3 &&
            x.Filters.Contains(FilterTestConstants.FilterIdEquals100) &&
            x.Filters.Contains(FilterTestConstants.FilterLevelEqualsError) &&
            x.Filters.Contains(FilterTestConstants.FilterSourceContainsTest)));
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
            FilterTestConstants.FilterLevelEqualsError
        };

        var action = new ImportFavoritesAction([.. filtersToImport]);

        // Act
        await effects.HandleImportFavorites(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<AddFavoriteFilterSuccessAction>(a =>
            a.Filters.Count == 2 &&
            a.Filters.Contains("Id == 100") &&
            a.Filters.Contains(FilterTestConstants.FilterLevelEqualsError)));
    }

    [Fact]
    public async Task HandleLoadFilters_ShouldLoadBothFavoritesAndRecent()
    {
        // Arrange
        var favoritesPreference = new List<string>
        {
            FilterTestConstants.FilterIdEquals100,
            FilterTestConstants.FilterLevelEqualsError
        };

        var recentPreference = new List<string>
        {
            FilterTestConstants.FilterSourceContainsTest
        };

        var mockPreferencesProvider = Substitute.For<IFilterCachePreferencesProvider>();
        mockPreferencesProvider.FavoriteFiltersPreference.Returns(favoritesPreference);
        mockPreferencesProvider.RecentFiltersPreference.Returns(recentPreference);

        var mockState = Substitute.For<IState<FilterCacheState>>();
        mockState.Value.Returns(new FilterCacheState());

        var effects = new Effects(mockPreferencesProvider, mockState);
        var mockDispatcher = Substitute.For<IDispatcher>();
        var action = new LoadFiltersAction();

        // Act
        await effects.HandleLoadFilters(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<LoadFiltersSuccessAction>(x =>
            x.FavoriteFilters.Count == 2 &&
            x.RecentFilters.Count() == 1 &&
            x.FavoriteFilters.Contains(FilterTestConstants.FilterIdEquals100) &&
            x.FavoriteFilters.Contains(FilterTestConstants.FilterLevelEqualsError) &&
            x.RecentFilters.Contains(FilterTestConstants.FilterSourceContainsTest)));
    }

    [Fact]
    public async Task HandleLoadFilters_WhenPreferencesEmpty_ShouldLoadEmptyLists()
    {
        // Arrange
        var mockPreferencesProvider = Substitute.For<IFilterCachePreferencesProvider>();
        mockPreferencesProvider.FavoriteFiltersPreference.Returns(new List<string>());
        mockPreferencesProvider.RecentFiltersPreference.Returns(new List<string>());

        var mockState = Substitute.For<IState<FilterCacheState>>();
        mockState.Value.Returns(new FilterCacheState());

        var effects = new Effects(mockPreferencesProvider, mockState);
        var mockDispatcher = Substitute.For<IDispatcher>();
        var action = new LoadFiltersAction();

        // Act
        await effects.HandleLoadFilters(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<LoadFiltersSuccessAction>(x =>
            x.FavoriteFilters.Count == 0 &&
            !x.RecentFilters.Any()));
    }

    [Fact]
    public async Task HandleRemoveFavoriteFilter_ShouldPersistBothPreferences()
    {
        // Arrange
        var existingFavorites = ImmutableList.Create(
            FilterTestConstants.FilterIdEquals100,
            FilterTestConstants.FilterLevelEqualsError);

        var existingRecent = ImmutableQueue.Create(FilterTestConstants.FilterSourceContainsTest);

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(
            existingFavorites,
            existingRecent);

        var action = new RemoveFavoriteFilterAction(FilterTestConstants.FilterIdEquals100);

        // Act
        await effects.HandleRemoveFavoriteFilter(action, mockDispatcher);

        // Assert
        _ = mockPreferencesProvider.Received(1).FavoriteFiltersPreference = Arg.Is<IEnumerable<string>>(x =>
            !x.Contains(FilterTestConstants.FilterIdEquals100) &&
            x.Contains(FilterTestConstants.FilterLevelEqualsError));

        _ = mockPreferencesProvider.Received(1).RecentFiltersPreference = Arg.Is<IEnumerable<string>>(x =>
            x.Contains(FilterTestConstants.FilterIdEquals100));
    }

    [Fact]
    public async Task HandleRemoveFavoriteFilter_WhenFilterInRecent_ShouldRemoveFromFavoritesOnly()
    {
        // Arrange
        var existingFavorites = ImmutableList.Create(
            FilterTestConstants.FilterIdEquals100,
            FilterTestConstants.FilterLevelEqualsError);

        var existingRecent = ImmutableQueue.Create(FilterTestConstants.FilterIdEquals100);

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(
            existingFavorites,
            existingRecent);

        var action = new RemoveFavoriteFilterAction(FilterTestConstants.FilterIdEquals100);

        // Act
        await effects.HandleRemoveFavoriteFilter(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<RemoveFavoriteFilterSuccessAction>(x =>
            x.FavoriteFilters.Count == 1 &&
            !x.FavoriteFilters.Contains(FilterTestConstants.FilterIdEquals100) &&
            x.RecentFilters.Count() == 1 &&
            x.RecentFilters.Contains(FilterTestConstants.FilterIdEquals100)));
    }

    [Fact]
    public async Task HandleRemoveFavoriteFilter_WhenFilterNotInFavorites_ShouldNotDispatch()
    {
        // Arrange
        var existingFavorites = ImmutableList.Create(FilterTestConstants.FilterIdEquals100);

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(
            existingFavorites);

        var action = new RemoveFavoriteFilterAction(FilterTestConstants.FilterLevelEqualsError);

        // Act
        await effects.HandleRemoveFavoriteFilter(action, mockDispatcher);

        // Assert
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<RemoveFavoriteFilterSuccessAction>());
    }

    [Fact]
    public async Task HandleRemoveFavoriteFilter_WhenFilterNotInRecent_ShouldAddToRecent()
    {
        // Arrange
        var existingFavorites = ImmutableList.Create(
            FilterTestConstants.FilterIdEquals100,
            FilterTestConstants.FilterLevelEqualsError);

        var existingRecent = ImmutableQueue.Create(FilterTestConstants.FilterSourceContainsTest);

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(
            existingFavorites,
            existingRecent);

        var action = new RemoveFavoriteFilterAction(FilterTestConstants.FilterIdEquals100);

        // Act
        await effects.HandleRemoveFavoriteFilter(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<RemoveFavoriteFilterSuccessAction>(x =>
            x.FavoriteFilters.Count == 1 &&
            !x.FavoriteFilters.Contains(FilterTestConstants.FilterIdEquals100) &&
            x.RecentFilters.Count() == 2 &&
            x.RecentFilters.Contains(FilterTestConstants.FilterIdEquals100)));
    }

    [Fact]
    public async Task HandleRemoveFavoriteFilter_WhenRecentIsFull_ShouldDequeueOldest()
    {
        // Arrange
        var existingFavorites = ImmutableList.Create(FilterTestConstants.FilterIdEquals100);

        var filters = Enumerable.Range(1, 20)
            .Select(i => $"Filter{i}")
            .ToList();

        var existingRecent = ImmutableQueue.CreateRange(filters);

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(
            existingFavorites,
            existingRecent);

        var action = new RemoveFavoriteFilterAction(FilterTestConstants.FilterIdEquals100);

        // Act
        await effects.HandleRemoveFavoriteFilter(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<RemoveFavoriteFilterSuccessAction>(x =>
            x.FavoriteFilters.Count == 0 &&
            x.RecentFilters.Count() == 20 &&
            !x.RecentFilters.Contains("Filter1") &&
            x.RecentFilters.Contains(FilterTestConstants.FilterIdEquals100)));
    }

    private static (Effects effects, IDispatcher mockDispatcher, IFilterCachePreferencesProvider mockPreferencesProvider) CreateEffects(
        ImmutableList<string>? favoriteFilters = null,
        ImmutableQueue<string>? recentFilters = null)
    {
        var mockState = Substitute.For<IState<FilterCacheState>>();

        mockState.Value.Returns(new FilterCacheState
        {
            FavoriteFilters = favoriteFilters ?? ImmutableList<string>.Empty,
            RecentFilters = recentFilters ?? ImmutableQueue<string>.Empty
        });

        var mockPreferencesProvider = Substitute.For<IFilterCachePreferencesProvider>();

        var effects = new Effects(mockPreferencesProvider, mockState);
        var mockDispatcher = Substitute.For<IDispatcher>();

        return (effects, mockDispatcher, mockPreferencesProvider);
    }
}
