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
    private static readonly HighlightFilterSelector s_selector = new();

    [Fact]
    public void ComputeHighlightKey_WhenCandidateAdded_ReturnsDifferentKey()
    {
        var first = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals100,
            HighlightColor.Red,
            true);

        var second = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals200,
            HighlightColor.Blue,
            true);

        Assert.NotEqual(
            s_selector.ComputeHighlightKey(ImmutableList.Create(first)),
            s_selector.ComputeHighlightKey(ImmutableList.Create(first, second)));
    }

    [Fact]
    public void ComputeHighlightKey_WhenCandidatesReordered_ReturnsDifferentKey()
    {
        // Order is significant because GetHighlight is first-match-wins: swapping two candidates
        // can change which color an event ends up with.
        var first = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals100,
            HighlightColor.Red,
            true);

        var second = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals200,
            HighlightColor.Blue,
            true);

        Assert.NotEqual(
            s_selector.ComputeHighlightKey(ImmutableList.Create(first, second)),
            s_selector.ComputeHighlightKey(ImmutableList.Create(second, first)));
    }

    [Fact]
    public void ComputeHighlightKey_WhenColorChangesWithinDefinedRange_ReturnsDifferentKey()
    {
        var red = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals100,
            HighlightColor.Red,
            true);

        var blue = red with { Color = HighlightColor.Blue };

        Assert.NotEqual(
            s_selector.ComputeHighlightKey(ImmutableList.Create(red)),
            s_selector.ComputeHighlightKey(ImmutableList.Create(blue)));
    }

    [Fact]
    public void ComputeHighlightKey_WhenColorMovesOutOfDefinedRange_ReturnsDifferentKey()
    {
        // A filter whose color drops out of the enum range stops being a candidate (per
        // SelectFromFilters' Enum.IsDefined gate), so the candidate count drops and the key flips.
        var defined = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals100,
            HighlightColor.Red,
            true);

        var undefined = defined with { Color = (HighlightColor)9999 };

        Assert.NotEqual(
            s_selector.ComputeHighlightKey(ImmutableList.Create(defined)),
            s_selector.ComputeHighlightKey(ImmutableList.Create(undefined)));
    }

    [Fact]
    public void ComputeHighlightKey_WhenCompiledReferenceDiffers_ReturnsDifferentKey()
    {
        // Even when both filters target the same ComparisonText, every compile produces a fresh
        // CompiledFilter instance — and the hash uses RuntimeHelpers.GetHashCode (reference identity)
        // so a recompile invalidates the cache. This is the conservative semantic: the predicate
        // delegate may close over different state across compiles.
        var first = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals100,
            HighlightColor.Red,
            true);

        var second = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals100,
            HighlightColor.Red,
            true);

        Assert.NotSame(first.Compiled, second.Compiled);
        Assert.NotEqual(
            s_selector.ComputeHighlightKey(ImmutableList.Create(first)),
            s_selector.ComputeHighlightKey(ImmutableList.Create(second)));
    }

    [Fact]
    public void ComputeHighlightKey_WhenIdenticalFilters_ReturnsSameKey()
    {
        var keeper = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals100,
            HighlightColor.Red,
            true);

        var filters = ImmutableList.Create(keeper);

        Assert.Equal(
            s_selector.ComputeHighlightKey(filters),
            s_selector.ComputeHighlightKey(filters));
    }

    [Fact]
    public void ComputeHighlightKey_WhenIsEnabledToggled_ReturnsDifferentKey()
    {
        var enabled = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals100,
            HighlightColor.Red,
            true);

        var disabled = enabled with { IsEnabled = false };

        Assert.NotEqual(
            s_selector.ComputeHighlightKey(ImmutableList.Create(enabled)),
            s_selector.ComputeHighlightKey(ImmutableList.Create(disabled)));
    }

    [Fact]
    public void ComputeHighlightKey_WhenIsExcludedToggled_ReturnsDifferentKey()
    {
        var included = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals100,
            HighlightColor.Red,
            true);

        var excluded = included with { IsExcluded = true };

        Assert.NotEqual(
            s_selector.ComputeHighlightKey(ImmutableList.Create(included)),
            s_selector.ComputeHighlightKey(ImmutableList.Create(excluded)));
    }

    [Fact]
    public void ComputeHighlightKey_WhenNonHighlightFieldsChange_ReturnsSameKey()
    {
        // Mode, BasicFilter, Id, and ComparisonText (when Compiled is unchanged) do not affect
        // SelectHighlightCandidates — so the change-key must not flip on those, otherwise the
        // optimization gives no benefit on common Mode-toggle / re-id workflows.
        var original = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals100,
            HighlightColor.Red,
            true);

        var withDifferentMode = original with { Mode = FilterMode.Cached };
        var withDifferentId = original with { Id = FilterId.Create() };
        var withDifferentText = original with { ComparisonText = "Id == 12345" };

        int originalKey = s_selector.ComputeHighlightKey(ImmutableList.Create(original));
        Assert.Equal(originalKey, s_selector.ComputeHighlightKey(ImmutableList.Create(withDifferentMode)));
        Assert.Equal(originalKey, s_selector.ComputeHighlightKey(ImmutableList.Create(withDifferentId)));
        Assert.Equal(originalKey, s_selector.ComputeHighlightKey(ImmutableList.Create(withDifferentText)));
    }

    [Fact]
    public void ComputeHighlightKey_WhenSkippedFilterFieldsChange_ReturnsSameKey()
    {
        // The whole point of the optimization: editing a non-candidate filter (disabled / excluded /
        // null-Compiled / undefined-color) must not invalidate the highlight cache, since none of
        // those filters ever contribute to GetHighlight.
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

        Assert.NotSame(disabledOriginal.Compiled, disabledWithDifferentCompiled.Compiled);

        Assert.Equal(
            s_selector.ComputeHighlightKey(ImmutableList.Create(keeper, disabledOriginal)),
            s_selector.ComputeHighlightKey(ImmutableList.Create(keeper, disabledWithDifferentCompiled)));
    }

    [Fact]
    public void ComputeHighlightKey_WhenSkippedFiltersReorderedAroundCandidate_ReturnsSameKey()
    {
        // Disabled filters' positions don't affect GetHighlight (they're filtered out before iteration),
        // so reordering them around a candidate must keep the key stable.
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

        Assert.Equal(
            s_selector.ComputeHighlightKey(ImmutableList.Create(disabledA, keeper, disabledB)),
            s_selector.ComputeHighlightKey(ImmutableList.Create(disabledB, keeper, disabledA)));
    }

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

        var fromEnabled = s_selector.SelectHighlightCandidates(enabled.Filters);
        var fromDisabled = s_selector.SelectHighlightCandidates(disabled.Filters);

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

        var result = s_selector.SelectHighlightCandidates(state.Filters);

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

        var result = s_selector.SelectHighlightCandidates(state.Filters);

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

        var result = s_selector.SelectHighlightCandidates(state.Filters);

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

        var result = s_selector.SelectHighlightCandidates(state.Filters);

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

        var result = s_selector.SelectHighlightCandidates(state.Filters);

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

        var result = s_selector.SelectHighlightCandidates(state.Filters);

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

        var result = s_selector.SelectHighlightCandidates(disabledPaneState.Filters);

        Assert.Single(result);
        Assert.Same(keeper, result[0]);
    }

    [Fact]
    public void SelectHighlightCandidates_WhenStateHasNoFilters_ShouldReturnEmpty()
    {
        var state = new FilterPaneState();

        var result = s_selector.SelectHighlightCandidates(state.Filters);

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

        var result = s_selector.SelectHighlightCandidates(state.Filters);

        Assert.Equal(2, result.Length);
        Assert.Same(firstKeeper, result[0]);
        Assert.Same(secondKeeper, result[1]);
    }
}
