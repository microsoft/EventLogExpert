// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Filtering.Runtime;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.Tests.TestUtils;
using EventLogExpert.Runtime.Tests.TestUtils.Constants;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Tests.FilterPane;

public sealed class HighlightSelectorTests
{
    private static readonly HighlightSelector s_selector = new();

    [Fact]
    public void ComputeHighlightKey_WhenCandidateAdded_ReturnsDifferentKey()
    {
        // Arrange
        var first = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals100,
            HighlightColor.Red,
            true);

        var second = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals200,
            HighlightColor.Blue,
            true);

        // Act + Assert
        Assert.NotEqual(
            s_selector.ComputeHighlightKey(ImmutableList.Create(first)),
            s_selector.ComputeHighlightKey(ImmutableList.Create(first, second)));
    }

    [Fact]
    public void ComputeHighlightKey_WhenCandidatesReordered_ReturnsDifferentKey()
    {
        // Arrange — GetHighlight is first-match-wins, so order is load-bearing.
        var first = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals100,
            HighlightColor.Red,
            true);

        var second = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals200,
            HighlightColor.Blue,
            true);

        // Act + Assert
        Assert.NotEqual(
            s_selector.ComputeHighlightKey(ImmutableList.Create(first, second)),
            s_selector.ComputeHighlightKey(ImmutableList.Create(second, first)));
    }

    [Fact]
    public void ComputeHighlightKey_WhenColorChangesWithinDefinedRange_ReturnsDifferentKey()
    {
        // Arrange
        var red = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals100,
            HighlightColor.Red,
            true);

        var blue = red with { Color = HighlightColor.Blue };

        // Act + Assert
        Assert.NotEqual(
            s_selector.ComputeHighlightKey(ImmutableList.Create(red)),
            s_selector.ComputeHighlightKey(ImmutableList.Create(blue)));
    }

    [Fact]
    public void ComputeHighlightKey_WhenColorMovesOutOfDefinedRange_ReturnsDifferentKey()
    {
        // Arrange
        var defined = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals100,
            HighlightColor.Red,
            true);

        var undefined = defined with { Color = (HighlightColor)9999 };

        // Act + Assert
        Assert.NotEqual(
            s_selector.ComputeHighlightKey(ImmutableList.Create(defined)),
            s_selector.ComputeHighlightKey(ImmutableList.Create(undefined)));
    }

    [Fact]
    public void ComputeHighlightKey_WhenCompiledReferenceDiffers_ReturnsDifferentKey()
    {
        // Arrange — hash uses RuntimeHelpers.GetHashCode (reference identity).
        var first = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals100,
            HighlightColor.Red,
            true);

        var second = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals100,
            HighlightColor.Red,
            true);

        // Act + Assert
        Assert.NotSame(first.Compiled, second.Compiled);
        Assert.NotEqual(
            s_selector.ComputeHighlightKey(ImmutableList.Create(first)),
            s_selector.ComputeHighlightKey(ImmutableList.Create(second)));
    }

    [Fact]
    public void ComputeHighlightKey_WhenIdenticalFilters_ReturnsSameKey()
    {
        // Arrange
        var keeper = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals100,
            HighlightColor.Red,
            true);

        var filters = ImmutableList.Create(keeper);

        // Act + Assert
        Assert.Equal(
            s_selector.ComputeHighlightKey(filters),
            s_selector.ComputeHighlightKey(filters));
    }

    [Fact]
    public void ComputeHighlightKey_WhenIsEnabledToggled_ReturnsDifferentKey()
    {
        // Arrange
        var enabled = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals100,
            HighlightColor.Red,
            true);

        var disabled = enabled with { IsEnabled = false };

        // Act + Assert
        Assert.NotEqual(
            s_selector.ComputeHighlightKey(ImmutableList.Create(enabled)),
            s_selector.ComputeHighlightKey(ImmutableList.Create(disabled)));
    }

    [Fact]
    public void ComputeHighlightKey_WhenIsExcludedToggled_ReturnsDifferentKey()
    {
        // Arrange
        var included = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals100,
            HighlightColor.Red,
            true);

        var excluded = included with { IsExcluded = true };

        // Act + Assert
        Assert.NotEqual(
            s_selector.ComputeHighlightKey(ImmutableList.Create(included)),
            s_selector.ComputeHighlightKey(ImmutableList.Create(excluded)));
    }

    [Fact]
    public void ComputeHighlightKey_WhenNonHighlightFieldsChange_ReturnsSameKey()
    {
        // Arrange
        var original = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals100,
            HighlightColor.Red,
            true);

        var withDifferentMode = original with { Mode = FilterMode.Cached };
        var withDifferentId = original with { Id = FilterId.Create() };
        var withDifferentText = original with { ComparisonText = "Id == 12345" };

        // Act
        int originalKey = s_selector.ComputeHighlightKey(ImmutableList.Create(original));

        // Assert
        Assert.Equal(originalKey, s_selector.ComputeHighlightKey(ImmutableList.Create(withDifferentMode)));
        Assert.Equal(originalKey, s_selector.ComputeHighlightKey(ImmutableList.Create(withDifferentId)));
        Assert.Equal(originalKey, s_selector.ComputeHighlightKey(ImmutableList.Create(withDifferentText)));
    }

    [Fact]
    public void ComputeHighlightKey_WhenSkippedFilterFieldsChange_ReturnsSameKey()
    {
        // Arrange
        var keeper = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals100,
            HighlightColor.Red,
            true);

        var disabledOriginal = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals200,
            HighlightColor.Blue);

        var disabledWithDifferentCompiled = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals999,
            HighlightColor.Green);

        // Act + Assert
        Assert.NotSame(disabledOriginal.Compiled, disabledWithDifferentCompiled.Compiled);

        Assert.Equal(
            s_selector.ComputeHighlightKey(ImmutableList.Create(keeper, disabledOriginal)),
            s_selector.ComputeHighlightKey(ImmutableList.Create(keeper, disabledWithDifferentCompiled)));
    }

    [Fact]
    public void ComputeHighlightKey_WhenSkippedFiltersReorderedAroundCandidate_ReturnsSameKey()
    {
        // Arrange
        var disabledA = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals100,
            HighlightColor.Red);

        var disabledB = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals200,
            HighlightColor.Blue);

        var keeper = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals999,
            HighlightColor.Green,
            true);

        // Act + Assert
        Assert.Equal(
            s_selector.ComputeHighlightKey(ImmutableList.Create(disabledA, keeper, disabledB)),
            s_selector.ComputeHighlightKey(ImmutableList.Create(disabledB, keeper, disabledA)));
    }

    [Fact]
    public void Select_ResultIsIdenticalAcrossIsEnabledToggle()
    {
        // Arrange
        var filters = ImmutableList.Create(
            FilterUtils.CreateTestFilter(
                Constants.FilterIdEquals100,
                HighlightColor.Red,
                true),
            FilterUtils.CreateTestFilter(
                Constants.FilterLevelEqualsError,
                HighlightColor.Yellow,
                true));

        var enabled = new FilterPaneState { IsEnabled = true, Filters = filters };
        var disabled = new FilterPaneState { IsEnabled = false, Filters = filters };

        // Act
        var fromEnabled = s_selector.Select(enabled.Filters);
        var fromDisabled = s_selector.Select(disabled.Filters);

        // Assert
        Assert.Equal(fromEnabled.Length, fromDisabled.Length);

        for (int i = 0; i < fromEnabled.Length; i++)
        {
            Assert.Same(fromEnabled[i], fromDisabled[i]);
        }
    }

    [Fact]
    public void Select_ShouldPreserveHighlightColorNoneCandidates()
    {
        // Arrange — None is enum-defined; the GetHighlight loop no-ops it later.
        var noneColored = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals100,
            HighlightColor.None,
            true);

        var state = new FilterPaneState { Filters = ImmutableList.Create(noneColored) };

        // Act
        var result = s_selector.Select(state.Filters);

        // Assert
        Assert.Single(result);
        Assert.Same(noneColored, result[0]);
    }

    [Fact]
    public void Select_ShouldReturnEnabledNonExcludedCompiledColoredFilters()
    {
        // Arrange
        var keeper = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals100,
            HighlightColor.Red,
            true);

        var state = new FilterPaneState { Filters = ImmutableList.Create(keeper) };

        // Act
        var result = s_selector.Select(state.Filters);

        // Assert
        Assert.Single(result);
        Assert.Same(keeper, result[0]);
    }

    [Fact]
    public void Select_ShouldSkipDisabledFilters()
    {
        // Arrange
        var disabled = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals100,
            HighlightColor.Red);

        var state = new FilterPaneState { Filters = ImmutableList.Create(disabled) };

        // Act
        var result = s_selector.Select(state.Filters);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void Select_ShouldSkipExcludedFilters()
    {
        // Arrange
        var excluded = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals100,
            HighlightColor.Red,
            true,
            true);

        var state = new FilterPaneState { Filters = ImmutableList.Create(excluded) };

        // Act
        var result = s_selector.Select(state.Filters);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void Select_ShouldSkipFiltersWithNullCompiled()
    {
        // Arrange
        var uncompiled = new SavedFilter
        {
            ComparisonText = string.Empty,
            Compiled = null,
            Color = HighlightColor.Red,
            IsEnabled = true,
            IsExcluded = false
        };

        var state = new FilterPaneState { Filters = ImmutableList.Create(uncompiled) };

        // Act
        var result = s_selector.Select(state.Filters);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void Select_ShouldSkipFiltersWithUndefinedColor()
    {
        // Arrange
        var keeper = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals100,
            HighlightColor.Red,
            true);

        var rogue = keeper with { Color = (HighlightColor)9999 };

        var state = new FilterPaneState { Filters = ImmutableList.Create(rogue) };

        // Act
        var result = s_selector.Select(state.Filters);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void Select_WhenFilterPaneDisabled_ShouldStillReturnEnabledColoredIncludeFilters()
    {
        // Arrange
        var keeper = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals100,
            HighlightColor.Red,
            true);

        var disabledPaneState = new FilterPaneState
        {
            IsEnabled = false,
            Filters = ImmutableList.Create(keeper)
        };

        // Act
        var result = s_selector.Select(disabledPaneState.Filters);

        // Assert
        Assert.Single(result);
        Assert.Same(keeper, result[0]);
    }

    [Fact]
    public void Select_WhenStateHasNoFilters_ShouldReturnEmpty()
    {
        // Arrange
        var state = new FilterPaneState();

        // Act
        var result = s_selector.Select(state.Filters);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void Select_WithMixedFilterList_ShouldReturnOnlyQualifyingCandidates()
    {
        // Arrange
        var disabled = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals100,
            HighlightColor.Red);

        var excluded = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals200,
            HighlightColor.Blue,
            true,
            true);

        var firstKeeper = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals999,
            HighlightColor.Green,
            true);

        var secondKeeper = FilterUtils.CreateTestFilter(
            Constants.FilterLevelEqualsError,
            HighlightColor.Yellow,
            true);

        var state = new FilterPaneState
        {
            Filters = ImmutableList.Create(disabled, excluded, firstKeeper, secondKeeper)
        };

        // Act
        var result = s_selector.Select(state.Filters);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Same(firstKeeper, result[0]);
        Assert.Same(secondKeeper, result[1]);
    }
}
