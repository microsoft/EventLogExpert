// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Structured;
using EventLogExpert.Filtering.Compilation;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Filtering.TestUtils;
using EventLogExpert.Filtering.TestUtils.Constants;
using System.Collections.Immutable;

namespace EventLogExpert.Filtering.Tests.Compilation;

public sealed class FilterServiceHighlightWinnerTests
{
    [Fact]
    public void ClassifyHighlightWinners_MatchesResolvedEventFirstMatchOracle()
    {
        ResolvedEvent[] events =
        [
            FilterEventBuilder.CreateTestEvent(100),
            FilterEventBuilder.CreateTestEvent(200, source: FilterTestConstants.EventSourceOtherSource),
            FilterEventBuilder.CreateTestEvent(300) with
            {
                UserDataIncomplete = true,
                UserData = ImmutableArray<UserDataField>.Empty
            }
        ];
        IEventColumnReader reader = ReaderFor(events);
        SavedFilter disabled = SavedFilter.TryCreate("Id == 300", color: HighlightColor.LightGreen, isEnabled: false)
            ?? throw new InvalidOperationException("Failed to compile disabled test filter.");
        SavedFilter excluded = (SavedFilter.TryCreate("Id == 300", color: HighlightColor.LightOrange, isEnabled: true)
            ?? throw new InvalidOperationException("Failed to compile excluded test filter.")) with
        {
            IsExcluded = true
        };
        SavedFilter[] orderedEligibleFilters =
        [
            CreateFilter("UserData[\"Missing\"] == \"x\"", HighlightColor.LightRed),
            CreateFilter(FilterTestConstants.FilterIdEquals100, HighlightColor.LightBlue),
            CreateFilter("Source == \"" + FilterTestConstants.EventSourceOtherSource + "\"", HighlightColor.LightYellow)
        ];
        _ = disabled;
        _ = excluded;

        byte[] winners = FilterService.ClassifyHighlightWinners(
            reader,
            [0, 1, 2],
            orderedEligibleFilters,
            TestContext.Current.CancellationToken);

        for (int index = 0; index < events.Length; index++)
        {
            Assert.Equal(OracleWinner(events[index], orderedEligibleFilters), winners[index]);
        }
    }

    [Fact]
    public void ClassifyHighlightWinners_WhenFilterColorIsNone_StillBlocksLaterColor()
    {
        IEventColumnReader reader = ReaderFor(
            FilterEventBuilder.CreateTestEvent(100, level: FilterTestConstants.EventLevelError));
        SavedFilter[] filters =
        [
            CreateFilter(FilterTestConstants.FilterIdEquals100, HighlightColor.None),
            CreateFilter(FilterTestConstants.FilterLevelEqualsError, HighlightColor.LightBlue)
        ];

        byte[] winners = FilterService.ClassifyHighlightWinners(reader, [0], filters, TestContext.Current.CancellationToken);

        Assert.Equal(1, winners[0]);
    }

    [Fact]
    public void ClassifyHighlightWinners_WhenFirstFilterDoesNotMatch_FallsThrough()
    {
        IEventColumnReader reader = ReaderFor(
            FilterEventBuilder.CreateTestEvent(200, level: FilterTestConstants.EventLevelError));
        SavedFilter[] filters =
        [
            CreateFilter(FilterTestConstants.FilterIdEquals100, HighlightColor.LightRed),
            CreateFilter(FilterTestConstants.FilterLevelEqualsError, HighlightColor.LightBlue)
        ];

        byte[] winners = FilterService.ClassifyHighlightWinners(reader, [0], filters, TestContext.Current.CancellationToken);

        Assert.Equal(2, winners[0]);
    }

    [Fact]
    public void ClassifyHighlightWinners_WhenRowIsNotSurviving_LeavesZero()
    {
        IEventColumnReader reader = ReaderFor(
            FilterEventBuilder.CreateTestEvent(100),
            FilterEventBuilder.CreateTestEvent(200));
        SavedFilter[] filters = [CreateFilter(FilterTestConstants.FilterIdEquals100, HighlightColor.LightRed)];

        byte[] winners = FilterService.ClassifyHighlightWinners(reader, [0], filters, TestContext.Current.CancellationToken);

        Assert.Equal(1, winners[0]);
        Assert.Equal(0, winners[1]);
    }

    [Fact]
    public void ClassifyHighlightWinners_WhenTwoFiltersMatch_UsesFirstMatch()
    {
        IEventColumnReader reader = ReaderFor(
            FilterEventBuilder.CreateTestEvent(100, level: FilterTestConstants.EventLevelError));
        SavedFilter[] filters =
        [
            CreateFilter(FilterTestConstants.FilterIdEquals100, HighlightColor.LightRed),
            CreateFilter(FilterTestConstants.FilterLevelEqualsError, HighlightColor.LightBlue)
        ];

        byte[] winners = FilterService.ClassifyHighlightWinners(reader, [0], filters, TestContext.Current.CancellationToken);

        Assert.Equal(1, winners[0]);
    }

    private static SavedFilter CreateFilter(string text, HighlightColor color) =>
        SavedFilter.TryCreate(text, color: color, isEnabled: true)
        ?? throw new InvalidOperationException($"Failed to compile test filter '{text}'.");

    private static byte OracleWinner(ResolvedEvent detail, IReadOnlyList<SavedFilter> filters)
    {
        for (int index = 0; index < filters.Count; index++)
        {
            if (filters[index].Compiled!.Predicate(detail)) { return (byte)(index + 1); }
        }

        return 0;
    }

    private static IEventColumnReader ReaderFor(params ResolvedEvent[] events) =>
        EventColumnStore.Build(events, generation: 0, contentVersion: 0)
            .CreateReader(EventLogId.Create());
}
