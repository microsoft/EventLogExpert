// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Filter;
using EventLogExpert.UI.FilterGroup;
using EventLogExpert.UI.Tests.TestUtils.Constants;
using Fluxor;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Tests.FilterGroup;

public sealed class EffectsTests
{
    [Fact]
    public async Task HandleAddGroup_ShouldPersistToPreferences()
    {
        // Arrange
        var groups = new List<SavedFilterGroup>
        {
            new() { Name = Constants.FilterGroupName }
        };

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(groups);

        // Act
        await effects.HandleAddGroup(mockDispatcher);

        // Assert
        _ = mockPreferencesProvider.Received(1).SavedFiltersPreference = Arg.Is<IEnumerable<SavedFilterGroup>>(x =>
            x.Count() == 1 && x.Any(y => y.Name == Constants.FilterGroupName));
    }

    [Fact]
    public async Task HandleAddGroup_WithEmptyGroups_ShouldPersistEmptyList()
    {
        // Arrange
        var groups = new List<SavedFilterGroup>();

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(groups);

        // Act
        await effects.HandleAddGroup(mockDispatcher);

        // Assert
        _ = mockPreferencesProvider.Received(1).SavedFiltersPreference = Arg.Is<IEnumerable<SavedFilterGroup>>(x =>
            !x.Any());
    }

    [Fact]
    public async Task HandleImportGroups_ShouldPersistToPreferences()
    {
        // Arrange
        var groups = new List<SavedFilterGroup>
        {
            new() { Name = Constants.FilterGroupName },
            new() { Name = Constants.FilterGroupNameNested }
        };

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(groups);

        // Act
        await effects.HandleImportGroups(mockDispatcher);

        // Assert
        _ = mockPreferencesProvider.Received(1).SavedFiltersPreference = Arg.Is<IEnumerable<SavedFilterGroup>>(x =>
            x.Count() == 2);
    }

    [Fact]
    public async Task HandleLoadGroups_ShouldDispatchLoadGroupsSuccess()
    {
        // Arrange
        var savedGroups = new List<SavedFilterGroup>
        {
            new() { Name = Constants.FilterGroupName },
            new() { Name = Constants.FilterGroupNameNested }
        };

        var mockPreferencesProvider = Substitute.For<IFilterGroupPreferencesProvider>();
        mockPreferencesProvider.SavedFiltersPreference.Returns(savedGroups);

        var mockState = Substitute.For<IState<FilterGroupState>>();
        mockState.Value.Returns(new FilterGroupState());

        var effects = new Effects(mockState, mockPreferencesProvider);
        var mockDispatcher = Substitute.For<IDispatcher>();

        // Act
        await effects.HandleLoadGroups(mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<LoadGroupsSuccessAction>(x =>
            x.Groups.Count() == 2 &&
            x.Groups.Any(g => g.Name == Constants.FilterGroupName) &&
            x.Groups.Any(g => g.Name == Constants.FilterGroupNameNested)));
    }

    [Fact]
    public async Task HandleLoadGroups_WhenPreferencesEmpty_ShouldDispatchEmptyList()
    {
        // Arrange
        var mockPreferencesProvider = Substitute.For<IFilterGroupPreferencesProvider>();
        mockPreferencesProvider.SavedFiltersPreference.Returns(new List<SavedFilterGroup>());

        var mockState = Substitute.For<IState<FilterGroupState>>();
        mockState.Value.Returns(new FilterGroupState());

        var effects = new Effects(mockState, mockPreferencesProvider);
        var mockDispatcher = Substitute.For<IDispatcher>();

        // Act
        await effects.HandleLoadGroups(mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<LoadGroupsSuccessAction>(x =>
            !x.Groups.Any()));
    }

    [Fact]
    public async Task HandleRemoveGroup_ShouldPersistToPreferences()
    {
        // Arrange
        var groups = new List<SavedFilterGroup>
        {
            new() { Name = Constants.FilterGroupName }
        };

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(groups);

        // Act
        await effects.HandleRemoveGroup(mockDispatcher);

        // Assert
        _ = mockPreferencesProvider.Received(1).SavedFiltersPreference = Arg.Is<IEnumerable<SavedFilterGroup>>(x =>
            x.Count() == 1);
    }

    [Fact]
    public async Task HandleSetGroup_ShouldPersistToPreferences()
    {
        // Arrange
        var groups = new List<SavedFilterGroup>
        {
            new() { Name = Constants.FilterGroupName }
        };

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(groups);

        // Act
        await effects.HandleSetGroup(mockDispatcher);

        // Assert
        _ = mockPreferencesProvider.Received(1).SavedFiltersPreference = Arg.Is<IEnumerable<SavedFilterGroup>>(g =>
            g.Any(x => x.Name == Constants.FilterGroupName));
    }

    [Fact]
    public async Task HandleSetGroup_WithMultipleGroups_ShouldPersistAll()
    {
        // Arrange
        var groups = new List<SavedFilterGroup>
        {
            new() { Name = Constants.FilterGroupName },
            new() { Name = Constants.FilterGroupNameNested },
            new() { Name = "Third Group" }
        };

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(groups);

        // Act
        await effects.HandleSetGroup(mockDispatcher);

        // Assert
        _ = mockPreferencesProvider.Received(1).SavedFiltersPreference = Arg.Is<IEnumerable<SavedFilterGroup>>(x =>
            x.Count() == 3);
    }

    private static (Effects effects, IDispatcher mockDispatcher, IFilterGroupPreferencesProvider mockPreferencesProvider)
        CreateEffects(List<SavedFilterGroup>? groups = null)
    {
        var mockState = Substitute.For<IState<FilterGroupState>>();

        mockState.Value.Returns(new FilterGroupState
        {
            Groups = ImmutableList.CreateRange(groups ?? [])
        });

        var mockPreferencesProvider = Substitute.For<IFilterGroupPreferencesProvider>();

        var effects = new Effects(mockState, mockPreferencesProvider);
        var mockDispatcher = Substitute.For<IDispatcher>();

        return (effects, mockDispatcher, mockPreferencesProvider);
    }
}
