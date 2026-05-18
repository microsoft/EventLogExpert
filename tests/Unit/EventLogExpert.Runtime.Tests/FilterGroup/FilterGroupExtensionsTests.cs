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
    public void AddFilterGroup_WhenGroupNamesIsNull_ShouldThrowArgumentNullException()
    {
        // Arrange
        var dictionary = ImmutableDictionary<string, FilterGroupNode>.Empty;
        var filterGroup = new SavedFilterGroup { Name = "Anything" };

        // Act + Assert
        Assert.Throws<ArgumentNullException>(() => dictionary.AddFilterGroup(null!, filterGroup));
    }

    [Fact]
    public void AddFilterGroup_WhenMixedShape_ShouldBucketByPathArity()
    {
        // Arrange
        var dictionary = ImmutableDictionary<string, FilterGroupNode>.Empty;
        var solo = new SavedFilterGroup { Name = "Solo" };
        var twoSegment = new SavedFilterGroup { Name = "Section\\Leaf" };
        var threeSegment = new SavedFilterGroup { Name = "Branch\\Sub\\Leaf" };

        // Act
        dictionary = dictionary.AddFilterGroup(["Solo"], solo);
        dictionary = dictionary.AddFilterGroup("Section\\Leaf".Split('\\'), twoSegment);
        dictionary = dictionary.AddFilterGroup("Branch\\Sub\\Leaf".Split('\\'), threeSegment);

        // Assert
        Assert.Single(dictionary[string.Empty].Groups);
        Assert.Single(dictionary["Section"].Groups);
        Assert.True(dictionary["Branch"].ChildNodes.ContainsKey("Sub"));
        Assert.True(dictionary["Branch"].ChildNodes["Sub"].ChildNodes.IsEmpty is false
            || dictionary["Branch"].ChildNodes["Sub"].Groups.Count > 0);
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
    public void AddFilterGroup_WhenPathHasEmptySegment_ShouldDescendThroughEmptyKey()
    {
        // Arrange
        var dictionary = ImmutableDictionary<string, FilterGroupNode>.Empty;
        var filterGroup = new SavedFilterGroup { Name = "Section\\\\Leaf" };

        // Act
        dictionary = dictionary.AddFilterGroup("Section\\\\Leaf".Split('\\'), filterGroup);

        // Assert
        Assert.True(dictionary["Section"].ChildNodes.ContainsKey(string.Empty));
    }

    [Fact]
    public void AddFilterGroup_WhenSameInstanceReAdded_ShouldDuplicateInGroupsBucket()
    {
        // Arrange
        var dictionary = ImmutableDictionary<string, FilterGroupNode>.Empty;
        var filterGroup = new SavedFilterGroup { Name = "Section\\Leaf" };

        // Act
        dictionary = dictionary.AddFilterGroup("Section\\Leaf".Split('\\'), filterGroup);
        dictionary = dictionary.AddFilterGroup("Section\\Leaf".Split('\\'), filterGroup);

        // Assert
        Assert.Equal(2, dictionary["Section"].Groups.Count);
    }

    [Fact]
    public void AddFilterGroup_WhenSameNameSiblings_ShouldKeepBothEntriesUnderTheSameKey()
    {
        // Arrange
        var dictionary = ImmutableDictionary<string, FilterGroupNode>.Empty;
        var first = new SavedFilterGroup { Name = "Section\\Leaf" };
        var second = new SavedFilterGroup { Name = "Section\\Leaf" };

        // Act
        dictionary = dictionary.AddFilterGroup("Section\\Leaf".Split('\\'), first);
        dictionary = dictionary.AddFilterGroup("Section\\Leaf".Split('\\'), second);

        // Assert
        Assert.Equal(2, dictionary["Section"].Groups.Count);
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

    [Fact]
    public void AddFilterGroup_WhenThreeLevelNesting_ShouldDescendFullPath()
    {
        // Arrange
        var dictionary = ImmutableDictionary<string, FilterGroupNode>.Empty;
        var filterGroup = new SavedFilterGroup { Name = "Root\\Mid\\Leaf" };

        // Act
        dictionary = dictionary.AddFilterGroup("Root\\Mid\\Leaf".Split('\\'), filterGroup);

        // Assert
        Assert.True(dictionary.ContainsKey("Root"));
        Assert.True(dictionary["Root"].ChildNodes.ContainsKey("Mid"));
    }
}
