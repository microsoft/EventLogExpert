// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Filtering.TestUtils;
using EventLogExpert.Filtering.TestUtils.Constants;
using EventLogExpert.Runtime.FilterPane;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Tests.FilterPane;

public sealed class HighlightSelectorTests
{
    private static readonly HighlightSelector s_selector = new();

    [Fact]
    public void ComputeHighlightKey_TreatsUserDataFilterAsEligible()
    {
        // Arrange
        var userData = FilterBuilder.CreateTestFilter(
            "UserData[\"Foo\"] == \"x\"",
            HighlightColor.Red,
            true);

        var scalar = FilterBuilder.CreateTestFilter(
            FilterTestConstants.FilterIdEquals100,
            HighlightColor.Blue,
            true);

        // Act + Assert: an eligible UserData filter contributes to the key, so adding it changes the key.
        Assert.NotEqual(
            s_selector.ComputeHighlightKey(ImmutableList.Create(scalar)),
            s_selector.ComputeHighlightKey(ImmutableList.Create(scalar, userData)));
    }

    [Fact]
    public void ComputeHighlightKey_WhenCandidateAdded_ReturnsDifferentKey()
    {
        // Arrange
        var first = FilterBuilder.CreateTestFilter(
            FilterTestConstants.FilterIdEquals100,
            HighlightColor.Red,
            true);

        var second = FilterBuilder.CreateTestFilter(
            FilterTestConstants.FilterIdEquals200,
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
        // Arrange - GetHighlight is first-match-wins; ordering is load-bearing.
        var first = FilterBuilder.CreateTestFilter(
            FilterTestConstants.FilterIdEquals100,
            HighlightColor.Red,
            true);

        var second = FilterBuilder.CreateTestFilter(
            FilterTestConstants.FilterIdEquals200,
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
        var red = FilterBuilder.CreateTestFilter(
            FilterTestConstants.FilterIdEquals100,
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
        var defined = FilterBuilder.CreateTestFilter(
            FilterTestConstants.FilterIdEquals100,
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
        // Arrange - key hashes Compiled by reference identity (RuntimeHelpers.GetHashCode).
        var first = FilterBuilder.CreateTestFilter(
            FilterTestConstants.FilterIdEquals100,
            HighlightColor.Red,
            true);

        var second = FilterBuilder.CreateTestFilter(
            FilterTestConstants.FilterIdEquals100,
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
        var keeper = FilterBuilder.CreateTestFilter(
            FilterTestConstants.FilterIdEquals100,
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
        var enabled = FilterBuilder.CreateTestFilter(
            FilterTestConstants.FilterIdEquals100,
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
        var included = FilterBuilder.CreateTestFilter(
            FilterTestConstants.FilterIdEquals100,
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
        var original = FilterBuilder.CreateTestFilter(
            FilterTestConstants.FilterIdEquals100,
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
        var keeper = FilterBuilder.CreateTestFilter(
            FilterTestConstants.FilterIdEquals100,
            HighlightColor.Red,
            true);

        var disabledOriginal = FilterBuilder.CreateTestFilter(
            FilterTestConstants.FilterIdEquals200,
            HighlightColor.Blue);

        var disabledWithDifferentCompiled = FilterBuilder.CreateTestFilter(
            FilterTestConstants.FilterIdEquals999,
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
        var disabledA = FilterBuilder.CreateTestFilter(
            FilterTestConstants.FilterIdEquals100,
            HighlightColor.Red);

        var disabledB = FilterBuilder.CreateTestFilter(
            FilterTestConstants.FilterIdEquals200,
            HighlightColor.Blue);

        var keeper = FilterBuilder.CreateTestFilter(
            FilterTestConstants.FilterIdEquals999,
            HighlightColor.Green,
            true);

        // Act + Assert
        Assert.Equal(
            s_selector.ComputeHighlightKey(ImmutableList.Create(disabledA, keeper, disabledB)),
            s_selector.ComputeHighlightKey(ImmutableList.Create(disabledB, keeper, disabledA)));
    }

    [Fact]
    public void ComputePredicatePlanKey_WhenOnlyColorChanges_ReturnsSameKey()
    {
        var red = FilterBuilder.CreateTestFilter(
            FilterTestConstants.FilterIdEquals100,
            HighlightColor.Red,
            true);
        var blue = red with { Color = HighlightColor.Blue };

        Assert.Equal(
            s_selector.ComputePredicatePlanKey(ImmutableList.Create(red)),
            s_selector.ComputePredicatePlanKey(ImmutableList.Create(blue)));
    }

    [Fact]
    public void ComputePredicatePlanKey_WhenPredicateOrStateChanges_ReturnsDifferentKey()
    {
        var original = FilterBuilder.CreateTestFilter(
            FilterTestConstants.FilterIdEquals100,
            HighlightColor.Red,
            true);
        var predicateChanged = FilterBuilder.CreateTestFilter(
            FilterTestConstants.FilterIdEquals200,
            HighlightColor.Red,
            true);
        var disabled = original with { IsEnabled = false };
        var excluded = original with { IsExcluded = true };

        int key = s_selector.ComputePredicatePlanKey(ImmutableList.Create(original));

        Assert.NotEqual(key, s_selector.ComputePredicatePlanKey(ImmutableList.Create(predicateChanged)));
        Assert.NotEqual(key, s_selector.ComputePredicatePlanKey(ImmutableList.Create(disabled)));
        Assert.NotEqual(key, s_selector.ComputePredicatePlanKey(ImmutableList.Create(excluded)));
    }

    [Fact]
    public void Select_ResultIsIdenticalAcrossIsEnabledToggle()
    {
        // Arrange
        var filters = ImmutableList.Create(
            FilterBuilder.CreateTestFilter(
                FilterTestConstants.FilterIdEquals100,
                HighlightColor.Red,
                true),
            FilterBuilder.CreateTestFilter(
                FilterTestConstants.FilterLevelEqualsError,
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
    public void Select_ShouldIncludeUserDataFilters()
    {
        // A colored, enabled, non-excluded UserData filter is highlight-eligible and flows through Select like any other;
        // its tri-state Evaluate drives the highlight (an absent / Unknown result just leaves the row visible-but-uncolored).
        var userData = FilterBuilder.CreateTestFilter(
            "UserData[\"Foo\"] == \"x\"",
            HighlightColor.Red,
            true);

        var scalar = FilterBuilder.CreateTestFilter(
            FilterTestConstants.FilterIdEquals100,
            HighlightColor.Blue,
            true);

        var state = new FilterPaneState { Filters = ImmutableList.Create(userData, scalar) };

        // Act
        var result = s_selector.Select(state.Filters);

        // Assert: both filters are highlight-eligible; the UserData filter is no longer gated out.
        Assert.Equal(2, result.Length);
        Assert.Same(userData, result[0]);
        Assert.Same(scalar, result[1]);
    }

    [Fact]
    public void Select_ShouldPreserveHighlightColorNoneCandidates()
    {
        // Arrange - None is enum-defined; the downstream GetHighlight loop no-ops it.
        var noneColored = FilterBuilder.CreateTestFilter(
            FilterTestConstants.FilterIdEquals100,
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
        var keeper = FilterBuilder.CreateTestFilter(
            FilterTestConstants.FilterIdEquals100,
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
        var disabled = FilterBuilder.CreateTestFilter(
            FilterTestConstants.FilterIdEquals100,
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
        var excluded = FilterBuilder.CreateTestFilter(
            FilterTestConstants.FilterIdEquals100,
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
        var keeper = FilterBuilder.CreateTestFilter(
            FilterTestConstants.FilterIdEquals100,
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
        var keeper = FilterBuilder.CreateTestFilter(
            FilterTestConstants.FilterIdEquals100,
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
        var disabled = FilterBuilder.CreateTestFilter(
            FilterTestConstants.FilterIdEquals100,
            HighlightColor.Red);

        var excluded = FilterBuilder.CreateTestFilter(
            FilterTestConstants.FilterIdEquals200,
            HighlightColor.Blue,
            true,
            true);

        var firstKeeper = FilterBuilder.CreateTestFilter(
            FilterTestConstants.FilterIdEquals999,
            HighlightColor.Green,
            true);

        var secondKeeper = FilterBuilder.CreateTestFilter(
            FilterTestConstants.FilterLevelEqualsError,
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
