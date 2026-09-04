// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.FilterLenses;
using Fluxor;
using NSubstitute;

namespace EventLogExpert.Runtime.Tests.FilterLenses;

public sealed class FilterLensCommandsTests
{
    [Fact]
    public void ClearLenses_DispatchesClear()
    {
        var dispatcher = Substitute.For<IDispatcher>();

        new FilterLensCommands(dispatcher).ClearLenses();

        dispatcher.Received(1).Dispatch(Arg.Any<ClearFilterLensesAction>());
    }

    [Fact]
    public void IncludeEventId_PushesNonNullLens()
    {
        var dispatcher = Substitute.For<IDispatcher>();

        new FilterLensCommands(dispatcher).IncludeEventId(4624);

        dispatcher.Received(1).Dispatch(Arg.Is<PushFilterLensAction>(action => action != null && action.Lens != null));
    }

    [Fact]
    public void IncludeValue_ProducesKeepOnlyLens_WithPositiveIncludePromoteForm()
    {
        var dispatcher = Substitute.For<IDispatcher>();

        new FilterLensCommands(dispatcher).IncludeValue(EventProperty.Source, "Contoso");

        dispatcher.Received(1).Dispatch(Arg.Is<PushFilterLensAction>(action =>
            action != null &&
            // Transient form stays an exclude-of-complement for master-toggle-independent live narrowing...
            action.Lens.ExcludeFilters.Count == 1 &&
            action.Lens.ExcludeFilters[0].IsExcluded &&
            // ...but the promote form is a single positive include that reads as the user's intent.
            action.Lens.PromoteFilters.Count == 1 &&
            !action.Lens.PromoteFilters[0].IsExcluded &&
            action.Lens.PromoteFilters[0].Compiled != null &&
            action.Lens.PromoteFilters[0].ComparisonText!.Contains("==")));
    }

    [Theory]
    [InlineData(ResolutionStatusTokens.NoProvider)]
    [InlineData(ResolutionStatusTokens.NoMessage)]
    [InlineData(ResolutionStatusTokens.Failed)]
    public void IncludeValue_ResolutionStatus_PushesNonNullLens(string token)
    {
        // The coverage cause-filters rely on this producing a lens; PushLens silently swallows a null, so a formatter
        // regression would make those buttons no-ops.
        var dispatcher = Substitute.For<IDispatcher>();

        new FilterLensCommands(dispatcher).IncludeValue(EventProperty.ResolutionStatus, token);

        dispatcher.Received(1).Dispatch(Arg.Is<PushFilterLensAction>(action => action != null && action.Lens != null));
    }

    [Fact]
    public void IncludeValue_User_KeepsTwoExcludeComplement_ButPromotesToSinglePositiveInclude()
    {
        var dispatcher = Substitute.For<IDispatcher>();

        new FilterLensCommands(dispatcher).IncludeValue(EventProperty.UserDisplayName, "CONTOSO\\jdoe");

        dispatcher.Received(1).Dispatch(Arg.Is<PushFilterLensAction>(action =>
            action != null &&
            // The presence-gated User keep-only lens needs TWO transient excludes (!= value + == null) to avoid
            // leaking no-user rows...
            action.Lens.ExcludeFilters.Count == 2 &&
            // ...but promotes to a SINGLE positive include, since an include drops a no-user (NoMatch) row on its own.
            action.Lens.PromoteFilters.Count == 1 &&
            !action.Lens.PromoteFilters[0].IsExcluded &&
            action.Lens.PromoteFilters[0].Compiled != null));
    }

    [Fact]
    public void PromoteAllLenses_DispatchesPromoteAll()
    {
        var dispatcher = Substitute.For<IDispatcher>();

        new FilterLensCommands(dispatcher).PromoteAllLenses();

        dispatcher.Received(1).Dispatch(Arg.Any<PromoteAllFilterLensesAction>());
    }

    [Fact]
    public void RemoveLens_DispatchesRemove()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var id = FilterLensId.Create();

        new FilterLensCommands(dispatcher).RemoveLens(id);

        dispatcher.Received(1).Dispatch(Arg.Is<RemoveFilterLensAction>(action =>
            action != null && action.Id == id));
    }

    [Fact]
    public void SaveLensesAsGroup_DispatchesSaveAsGroupWithName()
    {
        var dispatcher = Substitute.For<IDispatcher>();

        new FilterLensCommands(dispatcher).SaveLensesAsGroup("My Group");

        dispatcher.Received(1).Dispatch(Arg.Is<SaveLensesAsGroupAction>(action =>
            action != null && action.Name == "My Group"));
    }

    [Theory]
    [InlineData(30, "\u00b130s")]
    [InlineData(60, "\u00b11m")]
    [InlineData(300, "\u00b15m")]
    [InlineData(900, "\u00b115m")]
    [InlineData(3600, "\u00b11h")]
    public void ShowEventsNearTime_ChipSuffixMatchesOfferedDurations(int seconds, string expectedSuffix)
    {
        var dispatcher = Substitute.For<IDispatcher>();

        new FilterLensCommands(dispatcher).ShowEventsNearTime(
            new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc), TimeSpan.FromSeconds(seconds), TimeZoneInfo.Utc);

        dispatcher.Received(1).Dispatch(Arg.Is<PushFilterLensAction>(action =>
            action != null &&
            FilterLensLabelText.Invariant(action.Lens.Label).EndsWith(expectedSuffix, StringComparison.Ordinal)));
    }

    [Fact]
    public void ShowEventsNearTime_DispatchesPushWithCenteredEnabledWindow()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var anchor = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        new FilterLensCommands(dispatcher).ShowEventsNearTime(anchor, TimeSpan.FromMinutes(5), TimeZoneInfo.Utc);

        dispatcher.Received(1).Dispatch(Arg.Is<PushFilterLensAction>(action =>
            action != null &&
            action.Lens.Kind == LensKind.TimeWindow &&
            action.Lens.Window != null &&
            action.Lens.Window.IsEnabled &&
            action.Lens.Window.After == anchor.AddMinutes(-5) &&
            action.Lens.Window.Before == anchor.AddMinutes(5)));
    }

    [Fact]
    public void ShowEventsNearTime_InRangeNonStandardRadius_RendersLosslessChipSuffix()
    {
        var dispatcher = Substitute.For<IDispatcher>();

        new FilterLensCommands(dispatcher).ShowEventsNearTime(
            new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc), TimeSpan.FromSeconds(90), TimeZoneInfo.Utc);

        dispatcher.Received(1).Dispatch(Arg.Is<PushFilterLensAction>(action =>
            action != null &&
            FilterLensLabelText.Invariant(action.Lens.Label).EndsWith("\u00b190s", StringComparison.Ordinal)));
    }

    [Fact]
    public void ShowEventsNearTime_LabelRendersAnchorInDisplayZone()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var anchor = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var displayZone = TimeZoneInfo.CreateCustomTimeZone("test+5", TimeSpan.FromHours(5), "test+5", "test+5");
        var expectedAnchor = TimeZoneInfo.ConvertTimeFromUtc(anchor, displayZone);
        var expectedLabel = FilterLensLabelText.Invariant(
            new FilterLensLabel.TimeWindow(expectedAnchor, TimeSpan.FromMinutes(5)));

        new FilterLensCommands(dispatcher).ShowEventsNearTime(anchor, TimeSpan.FromMinutes(5), displayZone);

        dispatcher.Received(1).Dispatch(Arg.Is<PushFilterLensAction>(action =>
            action != null &&
            FilterLensLabelText.Invariant(action.Lens.Label) == expectedLabel));
    }

    [Fact]
    public void ShowEventsNearTime_MinValueAnchorWithNegativeOffsetZone_RendersLabelWithoutThrowing()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var westOfUtc = TimeZoneInfo.CreateCustomTimeZone("test-5", TimeSpan.FromHours(-5), "test-5", "test-5");

        new FilterLensCommands(dispatcher).ShowEventsNearTime(
            DateTime.MinValue, TimeSpan.FromSeconds(30), westOfUtc);

        dispatcher.Received(1).Dispatch(Arg.Any<PushFilterLensAction>());
    }

    [Fact]
    public void ShowEventsNearTime_MinValueAnchor_ClampsLowerBoundWithoutThrowing()
    {
        var dispatcher = Substitute.For<IDispatcher>();

        new FilterLensCommands(dispatcher).ShowEventsNearTime(
            DateTime.MinValue, TimeSpan.FromSeconds(30), TimeZoneInfo.Utc);

        dispatcher.Received(1).Dispatch(Arg.Is<PushFilterLensAction>(action =>
            action != null &&
            action.Lens.Window!.After == DateTime.MinValue &&
            action.Lens.Window.Before == DateTime.MinValue.AddSeconds(30)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    [InlineData(3601)]
    public void ShowEventsNearTime_RadiusOutsideSupportedRange_ThrowsAndDoesNotDispatch(int seconds)
    {
        var dispatcher = Substitute.For<IDispatcher>();

        Assert.Throws<ArgumentOutOfRangeException>(() => new FilterLensCommands(dispatcher).ShowEventsNearTime(
            new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc), TimeSpan.FromSeconds(seconds), TimeZoneInfo.Utc));

        dispatcher.DidNotReceive().Dispatch(Arg.Any<PushFilterLensAction>());
    }

    [Fact]
    public void ShowEventsNearTime_SubSecondRadius_ThrowsAndDoesNotDispatch()
    {
        var dispatcher = Substitute.For<IDispatcher>();

        Assert.Throws<ArgumentOutOfRangeException>(() => new FilterLensCommands(dispatcher).ShowEventsNearTime(
            new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc), TimeSpan.FromMilliseconds(500), TimeZoneInfo.Utc));

        dispatcher.DidNotReceive().Dispatch(Arg.Any<PushFilterLensAction>());
    }

    [Fact]
    public void ShowEventsNearTime_WithOriginLog_TagsLensWithThatLog()
    {
        var dispatcher = Substitute.For<IDispatcher>();

        new FilterLensCommands(dispatcher).ShowEventsNearTime(
            new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc), TimeSpan.FromMinutes(5), TimeZoneInfo.Utc, "LogA");

        dispatcher.Received(1).Dispatch(Arg.Is<PushFilterLensAction>(action =>
            action != null && action.Lens.OriginLog == "LogA"));
    }

    [Fact]
    public void ShowParentActivity_Guid_DispatchesActivityIdExcludeLensWithParentLabel()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var relatedActivityId = Guid.NewGuid();

        new FilterLensCommands(dispatcher).ShowParentActivity(relatedActivityId);

        dispatcher.Received(1).Dispatch(Arg.Is<PushFilterLensAction>(action =>
            action != null &&
            FilterLensLabelText.Invariant(action.Lens.Label) == $"Parent Activity = {relatedActivityId}" &&
            action.Lens.ExcludeFilters.Count == 1 &&
            action.Lens.ExcludeFilters[0].Compiled != null &&
            action.Lens.ExcludeFilters[0].ComparisonText!.Contains("ActivityId") &&
            !action.Lens.ExcludeFilters[0].ComparisonText!.Contains("RelatedActivityId")));
    }

    [Fact]
    public void ShowParentActivity_NullId_DoesNotDispatch()
    {
        var dispatcher = Substitute.For<IDispatcher>();

        new FilterLensCommands(dispatcher).ShowParentActivity(null);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<PushFilterLensAction>());
    }

    [Fact]
    public void ShowParentActivity_WithOriginLog_TagsLensWithThatLog()
    {
        var dispatcher = Substitute.For<IDispatcher>();

        new FilterLensCommands(dispatcher).ShowParentActivity(Guid.NewGuid(), "LogA");

        dispatcher.Received(1).Dispatch(Arg.Is<PushFilterLensAction>(action =>
            action != null && action.Lens.OriginLog == "LogA"));
    }

    [Fact]
    public void ShowRelatedByActivityId_Guid_DispatchesPushWithCompiledExcludeLens()
    {
        var dispatcher = Substitute.For<IDispatcher>();

        new FilterLensCommands(dispatcher).ShowRelatedByActivityId(Guid.NewGuid());

        dispatcher.Received(1).Dispatch(Arg.Is<PushFilterLensAction>(action =>
            action != null &&
            action.Lens.Kind == LensKind.Property &&
            action.Lens.ExcludeFilters.Count == 1 &&
            action.Lens.ExcludeFilters[0].IsExcluded &&
            action.Lens.ExcludeFilters[0].Compiled != null));
    }

    [Fact]
    public void ShowRelatedByActivityId_NullId_DoesNotDispatch()
    {
        var dispatcher = Substitute.For<IDispatcher>();

        new FilterLensCommands(dispatcher).ShowRelatedByActivityId(null);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<PushFilterLensAction>());
    }

    [Fact]
    public void ShowRelatedByActivityId_WithOriginLog_TagsLensWithThatLog()
    {
        var dispatcher = Substitute.For<IDispatcher>();

        new FilterLensCommands(dispatcher).ShowRelatedByActivityId(Guid.NewGuid(), "LogA");

        dispatcher.Received(1).Dispatch(Arg.Is<PushFilterLensAction>(action =>
            action != null && action.Lens.OriginLog == "LogA"));
    }

    [Fact]
    public void ShowRelatedByRelatedActivityId_Guid_DispatchesPushWithRelatedActivityIdExcludeLens()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var id = Guid.NewGuid();

        new FilterLensCommands(dispatcher).ShowRelatedByRelatedActivityId(id);

        dispatcher.Received(1).Dispatch(Arg.Is<PushFilterLensAction>(action =>
            action != null &&
            FilterLensLabelText.Invariant(action.Lens.Label) == $"Related Activity ID = {id}" &&
            action.Lens.ExcludeFilters.Count == 1 &&
            action.Lens.ExcludeFilters[0].IsExcluded &&
            action.Lens.ExcludeFilters[0].Compiled != null &&
            action.Lens.ExcludeFilters[0].ComparisonText!.Contains("RelatedActivityId")));
    }

    [Fact]
    public void ShowRelatedByRelatedActivityId_NullId_DoesNotDispatch()
    {
        var dispatcher = Substitute.For<IDispatcher>();

        new FilterLensCommands(dispatcher).ShowRelatedByRelatedActivityId(null);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<PushFilterLensAction>());
    }

    [Fact]
    public void ShowRelatedByRelatedActivityId_WithOriginLog_TagsLensWithThatLog()
    {
        var dispatcher = Substitute.For<IDispatcher>();

        new FilterLensCommands(dispatcher).ShowRelatedByRelatedActivityId(Guid.NewGuid(), "LogA");

        dispatcher.Received(1).Dispatch(Arg.Is<PushFilterLensAction>(action =>
            action != null && action.Lens.OriginLog == "LogA"));
    }

    [Fact]
    public void ShowTimeRange_DispatchesPushWithInclusiveEnabledUtcWindow()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var start = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2024, 1, 1, 13, 0, 0, DateTimeKind.Utc);

        new FilterLensCommands(dispatcher).ShowTimeRange(start, end, TimeZoneInfo.Utc);

        dispatcher.Received(1).Dispatch(Arg.Is<PushFilterLensAction>(action =>
            action != null &&
            action.Lens.Kind == LensKind.TimeWindow &&
            action.Lens.Window != null &&
            action.Lens.Window.IsEnabled &&
            action.Lens.Window.After == start &&
            action.Lens.Window.Before == end));
    }

    [Fact]
    public void ShowTimeRange_NormalizesAReversedRange()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var earlier = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var later = new DateTime(2024, 1, 1, 13, 0, 0, DateTimeKind.Utc);

        new FilterLensCommands(dispatcher).ShowTimeRange(later, earlier, TimeZoneInfo.Utc);

        dispatcher.Received(1).Dispatch(Arg.Is<PushFilterLensAction>(action =>
            action != null &&
            action.Lens.Window!.After == earlier &&
            action.Lens.Window.Before == later));
    }

    [Fact]
    public void ShowTimeRange_WithOriginLog_TagsLensWithThatLog()
    {
        var dispatcher = Substitute.For<IDispatcher>();

        new FilterLensCommands(dispatcher).ShowTimeRange(
            new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 1, 1, 13, 0, 0, DateTimeKind.Utc),
            TimeZoneInfo.Utc,
            "LogA");

        dispatcher.Received(1).Dispatch(Arg.Is<PushFilterLensAction>(action =>
            action != null && action.Lens.OriginLog == "LogA"));
    }
}
