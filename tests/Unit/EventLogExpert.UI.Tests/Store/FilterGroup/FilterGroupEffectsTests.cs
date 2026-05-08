// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.FilterGroup;
using EventLogExpert.UI.Tests.TestUtils.Constants;
using Fluxor;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Tests.Store.FilterGroup;

public sealed class FilterGroupEffectsTests
{
    [Fact]
    public async Task HandleAddGroup_ShouldPersistToPreferences()
    {
        // Arrange
        var groups = new List<FilterGroupModel>
        {
            new() { Name = Constants.FilterGroupName }
        };

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(groups);

        // Act
        await effects.HandleAddGroup(mockDispatcher);

        // Assert
        _ = mockPreferencesProvider.Received(1).SavedFiltersPreference = Arg.Is<IEnumerable<FilterGroupModel>>(x =>
            x.Count() == 1 && x.Any(y => y.Name == Constants.FilterGroupName));
    }

    [Fact]
    public async Task HandleAddGroup_WithEmptyGroups_ShouldPersistEmptyList()
    {
        // Arrange
        var groups = new List<FilterGroupModel>();

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(groups);

        // Act
        await effects.HandleAddGroup(mockDispatcher);

        // Assert
        _ = mockPreferencesProvider.Received(1).SavedFiltersPreference = Arg.Is<IEnumerable<FilterGroupModel>>(x =>
            !x.Any());
    }

    [Fact]
    public async Task HandleImportGroups_ShouldPersistToPreferences()
    {
        // Arrange
        var groups = new List<FilterGroupModel>
        {
            new() { Name = Constants.FilterGroupName },
            new() { Name = Constants.FilterGroupNameNested }
        };

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(groups);

        // Act
        await effects.HandleImportGroups(mockDispatcher);

        // Assert
        _ = mockPreferencesProvider.Received(1).SavedFiltersPreference = Arg.Is<IEnumerable<FilterGroupModel>>(x =>
            x.Count() == 2);
    }

    [Fact]
    public async Task HandleLoadGroups_ShouldDispatchLoadGroupsSuccess()
    {
        // Arrange
        var savedGroups = new List<FilterGroupModel>
        {
            new() { Name = Constants.FilterGroupName },
            new() { Name = Constants.FilterGroupNameNested }
        };

        var mockPreferencesProvider = Substitute.For<IPreferencesProvider>();
        mockPreferencesProvider.SavedFiltersPreference.Returns(savedGroups);

        var mockState = Substitute.For<IState<FilterGroupState>>();
        mockState.Value.Returns(new FilterGroupState());

        var effects = new FilterGroupEffects(mockState, mockPreferencesProvider);
        var mockDispatcher = Substitute.For<IDispatcher>();

        // Act
        await effects.HandleLoadGroups(mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<FilterGroupAction.LoadGroupsSuccess>(x =>
            x.Groups.Count() == 2 &&
            x.Groups.Any(g => g.Name == Constants.FilterGroupName) &&
            x.Groups.Any(g => g.Name == Constants.FilterGroupNameNested)));
    }

    [Fact]
    public async Task HandleLoadGroups_WhenPreferencesEmpty_ShouldDispatchEmptyList()
    {
        // Arrange
        var mockPreferencesProvider = Substitute.For<IPreferencesProvider>();
        mockPreferencesProvider.SavedFiltersPreference.Returns(new List<FilterGroupModel>());

        var mockState = Substitute.For<IState<FilterGroupState>>();
        mockState.Value.Returns(new FilterGroupState());

        var effects = new FilterGroupEffects(mockState, mockPreferencesProvider);
        var mockDispatcher = Substitute.For<IDispatcher>();

        // Act
        await effects.HandleLoadGroups(mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<FilterGroupAction.LoadGroupsSuccess>(x =>
            !x.Groups.Any()));
    }

    [Fact]
    public async Task HandleRemoveGroup_ShouldPersistToPreferences()
    {
        // Arrange
        var groups = new List<FilterGroupModel>
        {
            new() { Name = Constants.FilterGroupName }
        };

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(groups);

        // Act
        await effects.HandleRemoveGroup(mockDispatcher);

        // Assert
        _ = mockPreferencesProvider.Received(1).SavedFiltersPreference = Arg.Is<IEnumerable<FilterGroupModel>>(x =>
            x.Count() == 1);
    }

    [Fact]
    public async Task HandleSetGroup_ShouldPersistToPreferences()
    {
        // Arrange
        var groups = new List<FilterGroupModel>
        {
            new() { Name = Constants.FilterGroupName }
        };

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(groups);

        // Act
        await effects.HandleSetGroup(mockDispatcher);

        // Assert
        _ = mockPreferencesProvider.Received(1).SavedFiltersPreference = Arg.Is<IEnumerable<FilterGroupModel>>(g =>
            g.Any(x => x.Name == Constants.FilterGroupName));
    }

    [Fact]
    public async Task HandleSetGroup_WithMultipleGroups_ShouldPersistAll()
    {
        // Arrange
        var groups = new List<FilterGroupModel>
        {
            new() { Name = Constants.FilterGroupName },
            new() { Name = Constants.FilterGroupNameNested },
            new() { Name = "Third Group" }
        };

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(groups);

        // Act
        await effects.HandleSetGroup(mockDispatcher);

        // Assert
        _ = mockPreferencesProvider.Received(1).SavedFiltersPreference = Arg.Is<IEnumerable<FilterGroupModel>>(x =>
            x.Count() == 3);
    }

    private static (FilterGroupEffects effects, IDispatcher mockDispatcher, IPreferencesProvider mockPreferencesProvider)
        CreateEffects(List<FilterGroupModel>? groups = null)
    {
        var mockState = Substitute.For<IState<FilterGroupState>>();

        mockState.Value.Returns(new FilterGroupState
        {
            Groups = ImmutableList.CreateRange(groups ?? [])
        });

        var mockPreferencesProvider = Substitute.For<IPreferencesProvider>();

        var effects = new FilterGroupEffects(mockState, mockPreferencesProvider);
        var mockDispatcher = Substitute.For<IDispatcher>();

        return (effects, mockDispatcher, mockPreferencesProvider);
    }
}
