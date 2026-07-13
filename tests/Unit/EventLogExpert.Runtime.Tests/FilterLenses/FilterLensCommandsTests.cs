// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

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
    public void RemoveLens_DispatchesRemove()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var lens = new FilterLens { Label = "x", Kind = LensKind.Property };

        new FilterLensCommands(dispatcher).RemoveLens(lens);

        dispatcher.Received(1).Dispatch(Arg.Is<RemoveFilterLensAction>(action => action.Lens == lens));
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
            action.Lens.Label.EndsWith(expectedSuffix, StringComparison.Ordinal)));
    }

    [Fact]
    public void ShowEventsNearTime_DispatchesPushWithCenteredEnabledWindow()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var anchor = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        new FilterLensCommands(dispatcher).ShowEventsNearTime(anchor, TimeSpan.FromMinutes(5), TimeZoneInfo.Utc);

        dispatcher.Received(1).Dispatch(Arg.Is<PushFilterLensAction>(action =>
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

        // 90s is within (0, 1h] but not an offered duration; the suffix renders it losslessly as "90s", not a rounded "2m".
        new FilterLensCommands(dispatcher).ShowEventsNearTime(
            new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc), TimeSpan.FromSeconds(90), TimeZoneInfo.Utc);

        dispatcher.Received(1).Dispatch(Arg.Is<PushFilterLensAction>(action =>
            action.Lens.Label.EndsWith("\u00b190s", StringComparison.Ordinal)));
    }

    [Fact]
    public void ShowEventsNearTime_LabelRendersAnchorInDisplayZone()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var anchor = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var displayZone = TimeZoneInfo.CreateCustomTimeZone("test+5", TimeSpan.FromHours(5), "test+5", "test+5");
        var expectedAnchor = TimeZoneInfo.ConvertTimeFromUtc(anchor, displayZone);

        new FilterLensCommands(dispatcher).ShowEventsNearTime(anchor, TimeSpan.FromMinutes(5), displayZone);

        // Built with the same culture-sensitive ":T" the production label uses, so the assertion holds under any ambient
        // culture; the anchor renders in the +5 display zone (17:00:00), not UTC (12:00:00).
        dispatcher.Received(1).Dispatch(Arg.Is<PushFilterLensAction>(action =>
            action.Lens.Label == $"Near {expectedAnchor:T} \u00b15m"));
    }

    [Fact]
    public void ShowEventsNearTime_MinValueAnchor_ClampsLowerBoundWithoutThrowing()
    {
        var dispatcher = Substitute.For<IDispatcher>();

        // A degenerate default(DateTime) anchor must not throw - DateTime boundary arithmetic throws on underflow; the
        // lower bound clamps to DateTime.MinValue.
        new FilterLensCommands(dispatcher).ShowEventsNearTime(
            DateTime.MinValue, TimeSpan.FromSeconds(30), TimeZoneInfo.Utc);

        dispatcher.Received(1).Dispatch(Arg.Is<PushFilterLensAction>(action =>
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

        // The window is whole-second granularity; a sub-second radius (which the compact suffix cannot render losslessly)
        // is rejected rather than silently rounded.
        Assert.Throws<ArgumentOutOfRangeException>(() => new FilterLensCommands(dispatcher).ShowEventsNearTime(
            new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc), TimeSpan.FromMilliseconds(500), TimeZoneInfo.Utc));

        dispatcher.DidNotReceive().Dispatch(Arg.Any<PushFilterLensAction>());
    }

    [Fact]
    public void ShowEventsNearTime_MinValueAnchorWithNegativeOffsetZone_RendersLabelWithoutThrowing()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var westOfUtc = TimeZoneInfo.CreateCustomTimeZone("test-5", TimeSpan.FromHours(-5), "test-5", "test-5");

        // Guards the chip-label conversion for a degenerate default(DateTime) anchor against a non-UTC display zone: on
        // net10 TimeZoneInfo.ConvertTimeFromUtc clamps an out-of-range result to DateTime.MinValue instead of throwing, so
        // the label renders without crashing the context-menu handler. (Legacy .NET Framework threw for this exact case; a
        // future retarget that reintroduced the throw would fail this test rather than crashing at runtime.)
        new FilterLensCommands(dispatcher).ShowEventsNearTime(
            DateTime.MinValue, TimeSpan.FromSeconds(30), westOfUtc);

        dispatcher.Received(1).Dispatch(Arg.Any<PushFilterLensAction>());
    }

    [Fact]
    public void ShowEventsNearTime_WithOriginLog_TagsLensWithThatLog()
    {
        var dispatcher = Substitute.For<IDispatcher>();

        new FilterLensCommands(dispatcher).ShowEventsNearTime(
            new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc), TimeSpan.FromMinutes(5), TimeZoneInfo.Utc, "LogA");

        dispatcher.Received(1).Dispatch(Arg.Is<PushFilterLensAction>(action => action.Lens.OriginLog == "LogA"));
    }

    [Fact]
    public void ShowParentActivity_Guid_DispatchesActivityIdExcludeLensWithParentLabel()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var relatedActivityId = Guid.NewGuid();

        new FilterLensCommands(dispatcher).ShowParentActivity(relatedActivityId);

        dispatcher.Received(1).Dispatch(Arg.Is<PushFilterLensAction>(action =>
            action.Lens.Label == $"Parent Activity = {relatedActivityId}" &&
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

        dispatcher.Received(1).Dispatch(Arg.Is<PushFilterLensAction>(action => action.Lens.OriginLog == "LogA"));
    }

    [Fact]
    public void ShowRelatedByActivityId_Guid_DispatchesPushWithCompiledExcludeLens()
    {
        var dispatcher = Substitute.For<IDispatcher>();

        new FilterLensCommands(dispatcher).ShowRelatedByActivityId(Guid.NewGuid());

        dispatcher.Received(1).Dispatch(Arg.Is<PushFilterLensAction>(action =>
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

        dispatcher.Received(1).Dispatch(Arg.Is<PushFilterLensAction>(action => action.Lens.OriginLog == "LogA"));
    }

    [Fact]
    public void ShowRelatedByRelatedActivityId_Guid_DispatchesPushWithRelatedActivityIdExcludeLens()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var id = Guid.NewGuid();

        new FilterLensCommands(dispatcher).ShowRelatedByRelatedActivityId(id);

        dispatcher.Received(1).Dispatch(Arg.Is<PushFilterLensAction>(action =>
            action.Lens.Label == $"Related Activity ID = {id}" &&
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

        dispatcher.Received(1).Dispatch(Arg.Is<PushFilterLensAction>(action => action.Lens.OriginLog == "LogA"));
    }
}
