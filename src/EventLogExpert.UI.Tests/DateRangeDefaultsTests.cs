// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Tests.TestUtils;

namespace EventLogExpert.UI.Tests;

public sealed class DateRangeDefaultsTests
{
    private static readonly DateTime s_fallbackNow = new(2024, 6, 15, 9, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void ComputeFromActiveLogs_WhenAllLogsEmpty_ReturnsRoundedFallback()
    {
        var emptyLog = new EventLogData("Empty", PathType.LogName, []);

        var (after, before) = DateRangeDefaults.ComputeFromActiveLogs([emptyLog], s_fallbackNow);

        Assert.Equal(new DateTime(2024, 6, 15, 9, 0, 0, DateTimeKind.Utc), after);
        Assert.Equal(new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Utc), before);
    }

    [Fact]
    public void ComputeFromActiveLogs_WhenMixedEmptyAndPopulatedLogs_UsesPopulatedDataOnly()
    {
        var emptyLog = new EventLogData("Empty", PathType.LogName, []);
        var populated = CreateLog(
            "Populated",
            new DateTime(2024, 3, 10, 16, 20, 0, DateTimeKind.Utc),
            new DateTime(2024, 3, 10, 7, 50, 0, DateTimeKind.Utc));

        var (after, before) = DateRangeDefaults.ComputeFromActiveLogs([emptyLog, populated], s_fallbackNow);

        Assert.Equal(new DateTime(2024, 3, 10, 7, 0, 0, DateTimeKind.Utc), after);
        Assert.Equal(new DateTime(2024, 3, 10, 17, 0, 0, DateTimeKind.Utc), before);
    }

    [Fact]
    public void ComputeFromActiveLogs_WhenMultipleNonOverlappingLogs_ReturnsEnvelopeAndAfterPrecedesBefore()
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

        var (after, before) = DateRangeDefaults.ComputeFromActiveLogs([logA, logB], s_fallbackNow);

        Assert.Equal(new DateTime(2024, 1, 1, 4, 0, 0, DateTimeKind.Utc), after);
        Assert.Equal(new DateTime(2024, 1, 5, 22, 0, 0, DateTimeKind.Utc), before);
        Assert.True(after < before, "envelope bounds must not invert");
    }

    [Fact]
    public void ComputeFromActiveLogs_WhenMultipleOverlappingLogs_ReturnsEnvelope()
    {
        var logA = CreateLog(
            "A",
            new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 1, 1, 5, 30, 0, DateTimeKind.Utc));
        var logB = CreateLog(
            "B",
            new DateTime(2024, 1, 1, 18, 45, 0, DateTimeKind.Utc),
            new DateTime(2024, 1, 1, 9, 0, 0, DateTimeKind.Utc));

        var (after, before) = DateRangeDefaults.ComputeFromActiveLogs([logA, logB], s_fallbackNow);

        // After = MIN of all oldest = logA's 5:30 floored to 5:00
        Assert.Equal(new DateTime(2024, 1, 1, 5, 0, 0, DateTimeKind.Utc), after);
        // Before = MAX of all newest = logB's 18:45 ceilinged to 19:00
        Assert.Equal(new DateTime(2024, 1, 1, 19, 0, 0, DateTimeKind.Utc), before);
    }

    [Fact]
    public void ComputeFromActiveLogs_WhenNewestIsExactHour_DoesNotPushBeforeForward()
    {
        // Ceil of an exact-hour value must be the same value (no extra hour).
        var exactHour = new DateTime(2024, 1, 1, 14, 0, 0, DateTimeKind.Utc);
        var log = CreateLog("ExactHour", newest: exactHour, oldest: exactHour);

        var (after, before) = DateRangeDefaults.ComputeFromActiveLogs([log], s_fallbackNow);

        Assert.Equal(exactHour, after);
        Assert.Equal(exactHour, before);
    }

    [Fact]
    public void ComputeFromActiveLogs_WhenNoLogs_ReturnsRoundedFallback()
    {
        var (after, before) = DateRangeDefaults.ComputeFromActiveLogs([], s_fallbackNow);

        Assert.Equal(new DateTime(2024, 6, 15, 9, 0, 0, DateTimeKind.Utc), after);
        Assert.Equal(new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Utc), before);
    }

    [Fact]
    public void ComputeFromActiveLogs_WhenOldestIsExactHour_DoesNotPushAfterBackward()
    {
        var exactHour = new DateTime(2024, 1, 1, 8, 0, 0, DateTimeKind.Utc);
        var newer = new DateTime(2024, 1, 1, 9, 30, 0, DateTimeKind.Utc);
        var log = CreateLog("ExactHourOldest", newest: newer, oldest: exactHour);

        var (after, _) = DateRangeDefaults.ComputeFromActiveLogs([log], s_fallbackNow);

        Assert.Equal(exactHour, after);
    }

    [Fact]
    public void ComputeFromActiveLogs_WhenSingleLog_ReturnsItsBoundsRounded()
    {
        var log = CreateLog(
            "Single",
            new DateTime(2024, 1, 1, 14, 45, 30, DateTimeKind.Utc),
            new DateTime(2024, 1, 1, 8, 15, 10, DateTimeKind.Utc));

        var (after, before) = DateRangeDefaults.ComputeFromActiveLogs([log], s_fallbackNow);

        Assert.Equal(new DateTime(2024, 1, 1, 8, 0, 0, DateTimeKind.Utc), after);
        Assert.Equal(new DateTime(2024, 1, 1, 15, 0, 0, DateTimeKind.Utc), before);
    }

    private static EventLogData CreateLog(string name, DateTime newest, DateTime oldest)
    {
        // Events are stored newest-first (sorted by RecordId descending in production).
        var events = new List<DisplayEventModel>
        {
            EventUtils.CreateTestEvent(timeCreated: newest),
            EventUtils.CreateTestEvent(timeCreated: oldest)
        };

        return new EventLogData(name, PathType.LogName, events);
    }
}
