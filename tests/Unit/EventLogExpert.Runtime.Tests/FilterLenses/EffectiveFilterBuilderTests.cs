// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Compilation;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Filtering.TestUtils;
using EventLogExpert.Runtime.FilterLenses;

namespace EventLogExpert.Runtime.Tests.FilterLenses;

public sealed class EffectiveFilterBuilderTests
{
    private static readonly FilterService s_filterService = new();
    private static readonly Guid s_other = new("99999999-8888-7777-6666-555555555555");
    private static readonly Guid s_target = new("11111111-2222-3333-4444-555555555555");

    [Fact]
    public void Build_MultipleActivityLenses_AndTogether()
    {
        var matchBoth = FilterEventBuilder.CreateTestEvent(id: 1, activityId: s_target);
        var matchFirstOnly = FilterEventBuilder.CreateTestEvent(id: 2, activityId: s_target);

        // Two lenses for different Activity IDs AND together -> no event can match both distinct GUIDs -> empty.
        var composed = EffectiveFilterBuilder.Build(
            new Filter(null, []),
            [FilterLensFactory.ForActivityId(s_target)!, FilterLensFactory.ForActivityId(s_other)!]);

        var result = s_filterService.GetFilteredEvents([matchBoth, matchFirstOnly], composed);

        Assert.Empty(result);
    }

    [Fact]
    public void Build_NoLenses_ReturnsBaseUnchanged()
    {
        var baseFilter = new Filter(null, []);

        var composed = EffectiveFilterBuilder.Build(baseFilter, []);

        Assert.Equal(baseFilter, composed);
    }

    [Fact]
    public void Build_TimeWindowLens_IntersectsWithBaseDate_TwoSidedEnabled()
    {
        // Arrange - base date [10:00, 14:00]; lens window [12:00, 16:00]; intersection [12:00, 14:00].
        var inside = FilterEventBuilder.CreateTestEvent(id: 1, timeCreated: At(13));
        var beforeWindow = FilterEventBuilder.CreateTestEvent(id: 2, timeCreated: At(11));
        var afterBase = FilterEventBuilder.CreateTestEvent(id: 3, timeCreated: At(15));

        var baseFilter = new Filter(new DateFilter { After = At(10), Before = At(14), IsEnabled = true }, []);
        var windowLens = new FilterLens
        {
            Label = "window",
            Kind = LensKind.TimeWindow,
            Window = new DateFilter { After = At(12), Before = At(16), IsEnabled = true }
        };

        var composed = EffectiveFilterBuilder.Build(baseFilter, [windowLens]);

        Assert.NotNull(composed.DateFilter);
        Assert.True(composed.DateFilter!.IsEnabled);
        Assert.Equal(At(12), composed.DateFilter.After);
        Assert.Equal(At(14), composed.DateFilter.Before);

        var result = s_filterService.GetFilteredEvents([inside, beforeWindow, afterBase], composed);
        Assert.Equal([1], result.Select(e => e.Id).OrderBy(id => id));
    }

    [Fact]
    public void Build_TimeWindowLens_NullOrDisabledBase_UsesWindowVerbatim()
    {
        var windowLens = new FilterLens
        {
            Label = "window",
            Kind = LensKind.TimeWindow,
            Window = new DateFilter { After = At(12), Before = At(16), IsEnabled = true }
        };

        var nullBase = EffectiveFilterBuilder.Build(new Filter(null, []), [windowLens]);
        Assert.NotNull(nullBase.DateFilter);
        Assert.True(nullBase.DateFilter!.IsEnabled);
        Assert.Equal(At(12), nullBase.DateFilter.After);
        Assert.Equal(At(16), nullBase.DateFilter.Before);

        // Disabled base date is treated as unbounded, so the window applies verbatim.
        var disabledBase = EffectiveFilterBuilder.Build(
            new Filter(new DateFilter { After = At(1), Before = At(23), IsEnabled = false }, []),
            [windowLens]);
        Assert.Equal(At(12), disabledBase.DateFilter!.After);
        Assert.Equal(At(16), disabledBase.DateFilter.Before);
    }

    [Fact]
    public void Build_WithActivityIdLensOnBaseInclude_NarrowsToIntersection()
    {
        // Arrange - base includes Level == Error (OR-combined include list); the lens must AND-narrow, so only the
        // Error event that ALSO matches the ActivityId survives - not every Error, and not the matching-Activity Info.
        var errorMatch = FilterEventBuilder.CreateTestEvent(id: 1, level: "Error", activityId: s_target);
        var errorOther = FilterEventBuilder.CreateTestEvent(id: 2, level: "Error", activityId: s_other);
        var infoMatch = FilterEventBuilder.CreateTestEvent(id: 3, level: "Information", activityId: s_target);

        var levelInclude = SavedFilterFor("Level == \"Error\"");
        var baseFilter = new Filter(null, [levelInclude]);

        var composed = EffectiveFilterBuilder.Build(baseFilter, [FilterLensFactory.ForActivityId(s_target)!]);

        var result = s_filterService.GetFilteredEvents([errorMatch, errorOther, infoMatch], composed);

        Assert.Equal([1], result.Select(e => e.Id).OrderBy(id => id));
    }

    [Fact]
    public void Build_WithActivityIdLensOnEmptyBase_KeepsOnlyMatchingActivityId_HidesOtherAndAbsent()
    {
        // Arrange - the load-bearing behavior: exclude-of-complement (ActivityId != target) narrows to exactly
        // ActivityId == target, and because NotEqual on the nullable Guid is total (Match-for-null) the absent-ActivityId
        // event is HIDDEN, not leaked.
        var matching = FilterEventBuilder.CreateTestEvent(id: 1, activityId: s_target);
        var other = FilterEventBuilder.CreateTestEvent(id: 2, activityId: s_other);
        var absent = FilterEventBuilder.CreateTestEvent(id: 3, activityId: null);

        var lens = FilterLensFactory.ForActivityId(s_target);
        Assert.NotNull(lens);

        var composed = EffectiveFilterBuilder.Build(new Filter(null, []), [lens]);

        var result = s_filterService.GetFilteredEvents([matching, other, absent], composed);

        Assert.Equal([1], result.Select(e => e.Id).OrderBy(id => id));
    }

    [Fact]
    public void Build_DisabledWindowLens_DoesNotApplyDateFilter()
    {
        var disabledWindowLens = new FilterLens
        {
            Label = "window",
            Kind = LensKind.TimeWindow,
            Window = new DateFilter { After = At(12), Before = At(16), IsEnabled = false }
        };

        var composed = EffectiveFilterBuilder.Build(new Filter(null, []), [disabledWindowLens]);

        Assert.Null(composed.DateFilter);
    }

    private static DateTime At(int hourUtc) => new(2024, 1, 1, hourUtc, 0, 0, DateTimeKind.Utc);

    private static SavedFilter SavedFilterFor(string comparisonText) =>
        SavedFilter.TryCreate(comparisonText, isEnabled: true)
        ?? throw new InvalidOperationException($"test filter failed to compile: {comparisonText}");
}
