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
    public void Build_DisabledWindowLens_DoesNotApplyDateFilter()
    {
        var disabledWindowLens = new FilterLens
        {
            Label = new FilterLensLabel.TimeWindow(At(14), TimeSpan.FromHours(2)),
            Kind = LensKind.TimeWindow,
            Window = new DateFilter { After = At(12), Before = At(16), IsEnabled = false }
        };

        var composed = EffectiveFilterBuilder.Build(new Filter(null, []), [disabledWindowLens]);

        Assert.Null(composed.DateFilter);
    }

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
    public void Build_TimeWindowLensFromFactory_KeepsEventsWithinRadius_IncludingInclusiveBoundsAndSource()
    {
        // The factory-built centered window [T-1h, T+1h] keeps the source event at T and neighbors exactly on the
        // inclusive bounds, and hides anything past them.
        var anchor = At(12);
        var source = FilterEventBuilder.CreateTestEvent(id: 1, timeCreated: anchor);
        var onLowerBound = FilterEventBuilder.CreateTestEvent(id: 2, timeCreated: At(11));
        var onUpperBound = FilterEventBuilder.CreateTestEvent(id: 3, timeCreated: At(13));
        var belowWindow = FilterEventBuilder.CreateTestEvent(id: 4, timeCreated: At(10));
        var aboveWindow = FilterEventBuilder.CreateTestEvent(id: 5, timeCreated: At(14));

        var lens = FilterLensFactory.ForTimeWindow(anchor, TimeSpan.FromHours(1), TimeZoneInfo.Utc);
        var composed = EffectiveFilterBuilder.Build(new Filter(null, []), [lens]);

        var result = s_filterService.GetFilteredEvents(
            [source, onLowerBound, onUpperBound, belowWindow, aboveWindow], composed);

        Assert.Equal([1, 2, 3], result.Select(e => e.Id).OrderBy(id => id));
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
            Label = new FilterLensLabel.TimeWindow(At(14), TimeSpan.FromHours(2)),
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
            Label = new FilterLensLabel.TimeWindow(At(14), TimeSpan.FromHours(2)),
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
    public void Build_WithExcludeEventIdLens_HidesMatchingId_KeepsOthers()
    {
        var drop = FilterEventBuilder.CreateTestEvent(id: 4624);
        var keep = FilterEventBuilder.CreateTestEvent(id: 4625);

        var lens = FilterLensFactory.ForExcludedValue(EventProperty.Id, "4624");
        Assert.NotNull(lens);

        var composed = EffectiveFilterBuilder.Build(new Filter(null, []), [lens]);
        var result = s_filterService.GetFilteredEvents([drop, keep], composed);

        Assert.Equal([4625], result.Select(e => e.Id).OrderBy(id => id));
    }

    [Fact]
    public void Build_WithExcludeSourceLens_HidesMatchingSource_KeepsOthers()
    {
        var noisy1 = FilterEventBuilder.CreateTestEvent(id: 1, source: "NoisySource");
        var noisy2 = FilterEventBuilder.CreateTestEvent(id: 2, source: "NoisySource");
        var quiet = FilterEventBuilder.CreateTestEvent(id: 3, source: "QuietSource");

        var lens = FilterLensFactory.ForExcludedValue(EventProperty.Source, "NoisySource");
        Assert.NotNull(lens);
        var excludeLabel = Assert.IsType<FilterLensLabel.PropertyComparison>(lens!.Label);
        Assert.Equal(EventProperty.Source, excludeLabel.Property);
        Assert.False(excludeLabel.IsEqual);
        Assert.Equal("NoisySource", excludeLabel.Value);

        var composed = EffectiveFilterBuilder.Build(new Filter(null, []), [lens]);
        var result = s_filterService.GetFilteredEvents([noisy1, noisy2, quiet], composed);

        Assert.Equal([3], result.Select(e => e.Id).OrderBy(id => id));
    }

    [Fact]
    public void Build_WithExcludeUserLens_HidesMatchingUser_KeepsOthers()
    {
        // Locks that User (EventProperty.UserDisplayName) is actually filterable, so the User exclude button binds.
        var system = FilterEventBuilder.CreateTestEvent(id: 1, userDisplayName: "SYSTEM");
        var alice = FilterEventBuilder.CreateTestEvent(id: 2, userDisplayName: "CONTOSO\\alice");

        var lens = FilterLensFactory.ForExcludedValue(EventProperty.UserDisplayName, "SYSTEM");
        Assert.NotNull(lens);

        var composed = EffectiveFilterBuilder.Build(new Filter(null, []), [lens]);
        var result = s_filterService.GetFilteredEvents([system, alice], composed);

        Assert.Equal([2], result.Select(e => e.Id).OrderBy(id => id));
    }

    [Fact]
    public void Build_WithIncludeEventIdLens_KeepsMatchingId_HidesOthers()
    {
        var keep = FilterEventBuilder.CreateTestEvent(id: 4624);
        var drop = FilterEventBuilder.CreateTestEvent(id: 4625);

        var lens = FilterLensFactory.ForIncludedValue(EventProperty.Id, "4624");
        Assert.NotNull(lens);

        var composed = EffectiveFilterBuilder.Build(new Filter(null, []), [lens]);
        var result = s_filterService.GetFilteredEvents([keep, drop], composed);

        Assert.Equal([4624], result.Select(e => e.Id).OrderBy(id => id));
    }

    [Fact]
    public void Build_WithIncludeSourceLens_KeepsMatchingSource_HidesOthers()
    {
        var keep1 = FilterEventBuilder.CreateTestEvent(id: 1, source: "TargetSource");
        var keep2 = FilterEventBuilder.CreateTestEvent(id: 2, source: "TargetSource");
        var other = FilterEventBuilder.CreateTestEvent(id: 3, source: "OtherSource");

        var lens = FilterLensFactory.ForIncludedValue(EventProperty.Source, "TargetSource");
        Assert.NotNull(lens);
        var includeLabel = Assert.IsType<FilterLensLabel.PropertyComparison>(lens!.Label);
        Assert.Equal(EventProperty.Source, includeLabel.Property);
        Assert.True(includeLabel.IsEqual);
        Assert.Equal("TargetSource", includeLabel.Value);

        var composed = EffectiveFilterBuilder.Build(new Filter(null, []), [lens]);
        var result = s_filterService.GetFilteredEvents([keep1, keep2, other], composed);

        Assert.Equal([1, 2], result.Select(e => e.Id).OrderBy(id => id));
    }

    [Fact]
    public void Build_WithIncludeUserLens_KeepsMatchingUser_HidesOthersAndNoUserEvents()
    {
        var system = FilterEventBuilder.CreateTestEvent(id: 1, userDisplayName: "SYSTEM");
        var alice = FilterEventBuilder.CreateTestEvent(id: 2, userDisplayName: "CONTOSO\\alice");
        var noUser = FilterEventBuilder.CreateTestEvent(id: 3);

        var lens = FilterLensFactory.ForIncludedValue(EventProperty.UserDisplayName, "SYSTEM");
        Assert.NotNull(lens);

        var composed = EffectiveFilterBuilder.Build(new Filter(null, []), [lens]);
        var result = s_filterService.GetFilteredEvents([system, alice, noUser], composed);

        // The User field is presence-gated, so "User != SYSTEM" is NoMatch (kept) for the no-user event; the include
        // lens must add a second "User == null" exclude clause so keep-only genuinely drops it, not just other users.
        Assert.Equal([1], result.Select(e => e.Id).OrderBy(id => id));
    }

    [Fact]
    public void Build_WithParentActivityLens_NarrowsToActivityIdEqualsValue_NotRelatedActivityId()
    {
        // The parent-jump lens is an ActivityId-equality narrowing on the value: it keeps the parent event whose
        // ActivityId == target, NOT the child whose RelatedActivityId == target. This locks the field mapping.
        var parent = FilterEventBuilder.CreateTestEvent(id: 1, activityId: s_target);
        var child = FilterEventBuilder.CreateTestEvent(id: 2, activityId: s_other, relatedActivityId: s_target);
        var unrelated = FilterEventBuilder.CreateTestEvent(id: 3, activityId: s_other);

        var parentLens = FilterLensFactory.ForParentActivity(s_target)!;
        var composed = EffectiveFilterBuilder.Build(new Filter(null, []), [parentLens]);

        var result = s_filterService.GetFilteredEvents([parent, child, unrelated], composed);

        Assert.Equal([1], result.Select(e => e.Id).OrderBy(id => id));
    }

    [Fact]
    public void Build_WithRelatedActivityIdLensOnEmptyBase_KeepsOnlyMatching_HidesOtherAndAbsent()
    {
        // Mirror of the ActivityId absent-hidden proof for RelatedActivityId: exclude-of-complement
        // (RelatedActivityId != target) narrows to exactly RelatedActivityId == target, and because NotEqual on the
        // nullable Guid is total the absent-RelatedActivityId event is HIDDEN, not leaked. (Row-eval path via
        // GetFilteredEvents; column-path parity for the exclude is covered by ColumnEmitterParityTests.)
        var matching = FilterEventBuilder.CreateTestEvent(id: 1, relatedActivityId: s_target);
        var other = FilterEventBuilder.CreateTestEvent(id: 2, relatedActivityId: s_other);
        var absent = FilterEventBuilder.CreateTestEvent(id: 3, relatedActivityId: null);

        var lens = FilterLensFactory.ForRelatedActivityId(s_target);
        Assert.NotNull(lens);

        var composed = EffectiveFilterBuilder.Build(new Filter(null, []), [lens]);

        var result = s_filterService.GetFilteredEvents([matching, other, absent], composed);

        Assert.Equal([1], result.Select(e => e.Id).OrderBy(id => id));
    }

    [Fact]
    public void PromoteForm_IncludeUserLens_KeepsMatchingUser_DropsOthersAndNoUserEvents()
    {
        // The keep-only lens's PROMOTE form is a single positive include (User == SYSTEM). Unlike the transient
        // exclude-of-complement (which needs a second "User == null" clause), an include drops a no-user row on its own
        // (a decisive NoMatch), so the promoted pane view keeps exactly the same set as the live lens.
        var system = FilterEventBuilder.CreateTestEvent(id: 1, userDisplayName: "SYSTEM");
        var alice = FilterEventBuilder.CreateTestEvent(id: 2, userDisplayName: "CONTOSO\\alice");
        var noUser = FilterEventBuilder.CreateTestEvent(id: 3);

        var lens = FilterLensFactory.ForIncludedValue(EventProperty.UserDisplayName, "SYSTEM");
        Assert.NotNull(lens);
        Assert.Single(lens!.PromoteFilters);
        Assert.False(lens.PromoteFilters[0].IsExcluded);

        var result = s_filterService.GetFilteredEvents([system, alice, noUser], new Filter(null, lens.PromoteFilters));

        Assert.Equal([1], result.Select(e => e.Id).OrderBy(id => id));
    }

    private static DateTime At(int hourUtc) => new(2024, 1, 1, hourUtc, 0, 0, DateTimeKind.Utc);

    private static SavedFilter SavedFilterFor(string comparisonText) =>
        SavedFilter.TryCreate(comparisonText, isEnabled: true)
        ?? throw new InvalidOperationException($"test filter failed to compile: {comparisonText}");
}
