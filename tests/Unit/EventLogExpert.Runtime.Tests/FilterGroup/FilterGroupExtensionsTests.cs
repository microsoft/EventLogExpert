// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.FilterGroup;
using EventLogExpert.Runtime.Tests.TestUtils.Constants;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Tests.FilterGroup;

public sealed class FilterGroupExtensionsTests
{
    [Fact]
    public void AddFilterGroup_WhenAddingToEmptyDictionary_ShouldCreateNewEntry()
    {
        // Arrange
        var dictionary = ImmutableDictionary<string, FilterGroupNode>.Empty;
        var filterGroup = new SavedFilterGroup { Name = Constants.FilterGroupName };
        var groupNames = Constants.FilterGroupName.Split('\\');

        // Act
        var result = dictionary.AddFilterGroup(groupNames, filterGroup);

        // Assert
        Assert.Single(result);
        Assert.True(result.ContainsKey(Constants.FilterGroupSection));
    }

    [Fact]
    public void AddFilterGroup_WhenAddingToExistingSection_ShouldAppendFilterGroup()
    {
        // Arrange
        var dictionary = ImmutableDictionary<string, FilterGroupNode>.Empty;
        var filterGroup1 = new SavedFilterGroup { Name = Constants.FilterGroupName };
        var filterGroup2 = new SavedFilterGroup { Name = "TestSection\\AnotherGroup" };

        // Act
        dictionary = dictionary.AddFilterGroup(Constants.FilterGroupName.Split('\\'), filterGroup1);
        dictionary = dictionary.AddFilterGroup("TestSection\\AnotherGroup".Split('\\'), filterGroup2);

        // Assert
        Assert.Single(dictionary);
        Assert.Equal(2, dictionary[Constants.FilterGroupSection].Groups.Count);
    }

    [Fact]
    public void AddFilterGroup_WhenCalled_ShouldReturnUpdatedDictionary()
    {
        // Arrange
        var dictionary = ImmutableDictionary<string, FilterGroupNode>.Empty;
        var filterGroup = new SavedFilterGroup { Name = Constants.FilterGroupName };

        // Act
        var result = dictionary.AddFilterGroup(Constants.FilterGroupName.Split('\\'), filterGroup);

        // Assert
        Assert.NotSame(dictionary, result);
        Assert.Empty(dictionary);
        Assert.Single(result);
    }

    [Fact]
    public void AddFilterGroup_WhenNestedGroupNames_ShouldCreateHierarchy()
    {
        // Arrange
        var dictionary = ImmutableDictionary<string, FilterGroupNode>.Empty;
        var filterGroup = new SavedFilterGroup { Name = Constants.FilterGroupNameNested };
        var groupNames = Constants.FilterGroupNameNested.Split('\\');

        // Act
        dictionary = dictionary.AddFilterGroup(groupNames, filterGroup);

        // Assert
        Assert.True(dictionary.ContainsKey(Constants.FilterGroupSection));
        Assert.True(dictionary[Constants.FilterGroupSection].ChildNodes.ContainsKey(Constants.FilterGroupSubSection));
    }

    [Fact]
    public void AddFilterGroup_WhenSingleGroupName_ShouldAddToRoot()
    {
        // Arrange
        var dictionary = ImmutableDictionary<string, FilterGroupNode>.Empty;
        var filterGroup = new SavedFilterGroup { Name = "SingleGroup" };
        var groupNames = new[] { "SingleGroup" };

        // Act
        dictionary = dictionary.AddFilterGroup(groupNames, filterGroup);

        // Assert
        Assert.True(dictionary.ContainsKey(string.Empty));
        Assert.Single(dictionary[string.Empty].Groups);
    }
}
