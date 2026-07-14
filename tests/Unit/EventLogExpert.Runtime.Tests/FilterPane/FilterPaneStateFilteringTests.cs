// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.FilterPane;

namespace EventLogExpert.Runtime.Tests.FilterPane;

public sealed class FilterPaneStateFilteringTests
{
    private static readonly SavedFilter s_include =
        SavedFilter.TryCreate("Level == 4", isEnabled: true)
        ?? throw new InvalidOperationException("test filter failed to compile");
    private static readonly SavedFilter s_disabledInclude = s_include with { IsEnabled = false };

    private static readonly SavedFilter s_exclude = s_include with { IsExcluded = true };

    [Fact]
    public void IsFilteringEnabled_DisabledDateRangeNoFilters_IsFalse() =>
        AssertMatchesBuilder(
            new FilterPaneState { FilteredDateRange = new DateFilter { IsEnabled = false } },
            expected: false);

    [Fact]
    public void IsFilteringEnabled_DisabledWithExcludeFilter_IsTrue() =>
        AssertMatchesBuilder(new FilterPaneState { IsEnabled = false, Filters = [s_exclude] }, expected: true);

    [Fact]
    public void IsFilteringEnabled_DisabledWithOnlyIncludeFilter_IsFalse() =>
        AssertMatchesBuilder(new FilterPaneState { IsEnabled = false, Filters = [s_include] }, expected: false);

    [Fact]
    public void IsFilteringEnabled_Empty_IsFalse() =>
        AssertMatchesBuilder(new FilterPaneState(), expected: false);

    [Fact]
    public void IsFilteringEnabled_EnabledDateRange_IsTrue() =>
        AssertMatchesBuilder(
            new FilterPaneState { FilteredDateRange = new DateFilter { IsEnabled = true } },
            expected: true);

    [Fact]
    public void IsFilteringEnabled_EnabledWithEnabledFilter_IsTrue() =>
        AssertMatchesBuilder(new FilterPaneState { IsEnabled = true, Filters = [s_include] }, expected: true);

    [Fact]
    public void IsFilteringEnabled_EnabledWithOnlyDisabledFilter_IsFalse() =>
        AssertMatchesBuilder(new FilterPaneState { IsEnabled = true, Filters = [s_disabledInclude] }, expected: false);

    // The status bar's persistent-filter indicator selects FilterPaneState.IsFilteringEnabled, which must stay identical
    // to the composed FilterPaneFilterBuilder.Build(state).IsFilteringEnabled it derives from - locking that here prevents
    // the two from drifting.
    private static void AssertMatchesBuilder(FilterPaneState state, bool expected)
    {
        Assert.Equal(expected, state.IsFilteringEnabled);
        Assert.Equal(FilterPaneFilterBuilder.Build(state).IsFilteringEnabled, state.IsFilteringEnabled);
    }
}
