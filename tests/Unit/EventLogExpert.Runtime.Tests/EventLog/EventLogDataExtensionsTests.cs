// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.TestUtils;

namespace EventLogExpert.Runtime.Tests.EventLog;

public sealed class EventLogDataExtensionsTests
{
    private static readonly DateTime s_fallbackNow = new(2024, 6, 15, 9, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void GetEventDateRange_WhenAllLogsEmpty_ReturnsRoundedFallback()
    {
        var emptyLog = new EventLogData("Empty", LogPathType.Channel, []);

        var (after, before) = new[] { emptyLog }.GetEventDateRange(s_fallbackNow);

        Assert.Equal(new DateTime(2024, 6, 15, 9, 0, 0, DateTimeKind.Utc), after);
        Assert.Equal(new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Utc), before);
    }

    [Fact]
    public void GetEventDateRange_WhenMixedEmptyAndPopulatedLogs_UsesPopulatedDataOnly()
    {
        var emptyLog = new EventLogData("Empty", LogPathType.Channel, []);
        var populated = CreateLog(
            "Populated",
            new DateTime(2024, 3, 10, 16, 20, 0, DateTimeKind.Utc),
            new DateTime(2024, 3, 10, 7, 50, 0, DateTimeKind.Utc));

        var (after, before) = new[] { emptyLog, populated }.GetEventDateRange(s_fallbackNow);

        Assert.Equal(new DateTime(2024, 3, 10, 7, 0, 0, DateTimeKind.Utc), after);
        Assert.Equal(new DateTime(2024, 3, 10, 17, 0, 0, DateTimeKind.Utc), before);
    }

    [Fact]
    public void GetEventDateRange_WhenMultipleNonOverlappingLogs_ReturnsRangeAndAfterPrecedesBefore()
    {
        // Regression: previous (intersection) implementation would invert After/Before for non-overlapping logs.
        var logA = CreateLog(
            "A",
            new DateTime(2024, 1, 1, 6, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 1, 1, 4, 0, 0, DateTimeKind.Utc));
        var logB = CreateLog(
            "B",
            new DateTime(2024, 1, 5, 22, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 1, 5, 20, 0, 0, DateTimeKind.Utc));

        var (after, before) = new[] { logA, logB }.GetEventDateRange(s_fallbackNow);

        Assert.Equal(new DateTime(2024, 1, 1, 4, 0, 0, DateTimeKind.Utc), after);
        Assert.Equal(new DateTime(2024, 1, 5, 22, 0, 0, DateTimeKind.Utc), before);
        Assert.True(after < before, "range bounds must not invert");
    }

    [Fact]
    public void GetEventDateRange_WhenMultipleOverlappingLogs_ReturnsRange()
    {
        var logA = CreateLog(
            "A",
            new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 1, 1, 5, 30, 0, DateTimeKind.Utc));
        var logB = CreateLog(
            "B",
            new DateTime(2024, 1, 1, 18, 45, 0, DateTimeKind.Utc),
            new DateTime(2024, 1, 1, 9, 0, 0, DateTimeKind.Utc));

        var (after, before) = new[] { logA, logB }.GetEventDateRange(s_fallbackNow);

        // After = MIN of all oldest = logA's 5:30 floored to 5:00
        Assert.Equal(new DateTime(2024, 1, 1, 5, 0, 0, DateTimeKind.Utc), after);
        // Before = MAX of all newest = logB's 18:45 ceilinged to 19:00
        Assert.Equal(new DateTime(2024, 1, 1, 19, 0, 0, DateTimeKind.Utc), before);
    }

    [Fact]
    public void GetEventDateRange_WhenNewestIsExactHour_DoesNotPushBeforeForward()
    {
        // Ceil of an exact-hour value must be the same value (no extra hour).
        var exactHour = new DateTime(2024, 1, 1, 14, 0, 0, DateTimeKind.Utc);
        var log = CreateLog("ExactHour", exactHour, exactHour);

        var (after, before) = new[] { log }.GetEventDateRange(s_fallbackNow);

        Assert.Equal(exactHour, after);
        Assert.Equal(exactHour, before);
    }

    [Fact]
    public void GetEventDateRange_WhenNoLogs_ReturnsRoundedFallback()
    {
        var (after, before) = Array.Empty<EventLogData>().GetEventDateRange(s_fallbackNow);

        Assert.Equal(new DateTime(2024, 6, 15, 9, 0, 0, DateTimeKind.Utc), after);
        Assert.Equal(new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Utc), before);
    }

    [Fact]
    public void GetEventDateRange_WhenOldestIsExactHour_DoesNotPushAfterBackward()
    {
        var exactHour = new DateTime(2024, 1, 1, 8, 0, 0, DateTimeKind.Utc);
        var newer = new DateTime(2024, 1, 1, 9, 30, 0, DateTimeKind.Utc);
        var log = CreateLog("ExactHourOldest", newer, exactHour);

        var (after, _) = new[] { log }.GetEventDateRange(s_fallbackNow);

        Assert.Equal(exactHour, after);
    }

    [Fact]
    public void GetEventDateRange_WhenSingleLog_ReturnsItsBoundsRounded()
    {
        var log = CreateLog(
            "Single",
            new DateTime(2024, 1, 1, 14, 45, 30, DateTimeKind.Utc),
            new DateTime(2024, 1, 1, 8, 15, 10, DateTimeKind.Utc));

        var (after, before) = new[] { log }.GetEventDateRange(s_fallbackNow);

        Assert.Equal(new DateTime(2024, 1, 1, 8, 0, 0, DateTimeKind.Utc), after);
        Assert.Equal(new DateTime(2024, 1, 1, 15, 0, 0, DateTimeKind.Utc), before);
    }

    private static EventLogData CreateLog(string name, DateTime newest, DateTime oldest)
    {
        // Events are stored newest-first (sorted by RecordId descending in production).
        var events = new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(timeCreated: newest),
            FilterEventBuilder.CreateTestEvent(timeCreated: oldest)
        };

        return new EventLogData(name, LogPathType.Channel, events);
    }
}
