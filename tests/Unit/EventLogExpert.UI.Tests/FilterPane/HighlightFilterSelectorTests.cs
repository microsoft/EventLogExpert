// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Filter;
using EventLogExpert.UI.FilterPane;
using EventLogExpert.UI.Tests.TestUtils;
using EventLogExpert.UI.Tests.TestUtils.Constants;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Tests.FilterPane;

public sealed class HighlightFilterSelectorTests
{
    [Fact]
    public void SelectHighlightCandidates_ResultIsIdenticalAcrossIsEnabledToggle()
    {
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

        var fromEnabled = HighlightFilterSelector.SelectHighlightCandidates(enabled);
        var fromDisabled = HighlightFilterSelector.SelectHighlightCandidates(disabled);

        Assert.Equal(fromEnabled.Length, fromDisabled.Length);

        for (int i = 0; i < fromEnabled.Length; i++)
        {
            Assert.Same(fromEnabled[i], fromDisabled[i]);
        }
    }

    [Fact]
    public void SelectHighlightCandidates_ShouldPreserveHighlightColorNoneCandidates()
    {
        // HighlightColor.None is enum-defined, so the candidate list keeps it; the downstream
        // LogTablePane.GetHighlight loop then no-ops it via ToCssName() returning null.
        var noneColored = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals100,
            HighlightColor.None,
            true);

        var state = new FilterPaneState { Filters = ImmutableList.Create(noneColored) };

        var result = HighlightFilterSelector.SelectHighlightCandidates(state);

        Assert.Single(result);
        Assert.Same(noneColored, result[0]);
    }

    [Fact]
    public void SelectHighlightCandidates_ShouldReturnEnabledNonExcludedCompiledColoredFilters()
    {
        var keeper = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals100,
            HighlightColor.Red,
            true);

        var state = new FilterPaneState { Filters = ImmutableList.Create(keeper) };

        var result = HighlightFilterSelector.SelectHighlightCandidates(state);

        Assert.Single(result);
        Assert.Same(keeper, result[0]);
    }

    [Fact]
    public void SelectHighlightCandidates_ShouldSkipDisabledFilters()
    {
        var disabled = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals100,
            HighlightColor.Red);

        var state = new FilterPaneState { Filters = ImmutableList.Create(disabled) };

        var result = HighlightFilterSelector.SelectHighlightCandidates(state);

        Assert.Empty(result);
    }

    [Fact]
    public void SelectHighlightCandidates_ShouldSkipExcludedFilters()
    {
        var excluded = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals100,
            HighlightColor.Red,
            true,
            true);

        var state = new FilterPaneState { Filters = ImmutableList.Create(excluded) };

        var result = HighlightFilterSelector.SelectHighlightCandidates(state);

        Assert.Empty(result);
    }

    [Fact]
    public void SelectHighlightCandidates_ShouldSkipFiltersWithNullCompiled()
    {
        // A SavedFilter whose ComparisonText is empty has Compiled == null even when IsEnabled / Color
        // would otherwise qualify it as a highlight candidate.
        var uncompiled = new SavedFilter
        {
            ComparisonText = string.Empty,
            Compiled = null,
            Color = HighlightColor.Red,
            IsEnabled = true,
            IsExcluded = false
        };

        var state = new FilterPaneState { Filters = ImmutableList.Create(uncompiled) };

        var result = HighlightFilterSelector.SelectHighlightCandidates(state);

        Assert.Empty(result);
    }

    [Fact]
    public void SelectHighlightCandidates_ShouldSkipFiltersWithUndefinedColor()
    {
        // A persisted color value outside the HighlightColor enum range — produced by a future palette
        // mismatch or hand-edited persistence — must be skipped, mirroring LogTablePane.GetHighlight's
        // defensive `Enum.IsDefined` filter and matching WarnOnUnknownFilterColors's intent.
        var keeper = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals100,
            HighlightColor.Red,
            true);

        var rogue = keeper with { Color = (HighlightColor)9999 };

        var state = new FilterPaneState { Filters = ImmutableList.Create(rogue) };

        var result = HighlightFilterSelector.SelectHighlightCandidates(state);

        Assert.Empty(result);
    }

    [Fact]
    public void SelectHighlightCandidates_WhenFilterPaneDisabled_ShouldStillReturnEnabledColoredIncludeFilters()
    {
        var keeper = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals100,
            HighlightColor.Red,
            true);

        var disabledPaneState = new FilterPaneState
        {
            IsEnabled = false,
            Filters = ImmutableList.Create(keeper)
        };

        var result = HighlightFilterSelector.SelectHighlightCandidates(disabledPaneState);

        Assert.Single(result);
        Assert.Same(keeper, result[0]);
    }

    [Fact]
    public void SelectHighlightCandidates_WhenStateHasNoFilters_ShouldReturnEmpty()
    {
        var state = new FilterPaneState();

        var result = HighlightFilterSelector.SelectHighlightCandidates(state);

        Assert.Empty(result);
    }

    [Fact]
    public void SelectHighlightCandidates_WithMixedFilterList_ShouldReturnOnlyQualifyingCandidates()
    {
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

        var result = HighlightFilterSelector.SelectHighlightCandidates(state);

        Assert.Equal(2, result.Length);
        Assert.Same(firstKeeper, result[0]);
        Assert.Same(secondKeeper, result[1]);
    }
}
