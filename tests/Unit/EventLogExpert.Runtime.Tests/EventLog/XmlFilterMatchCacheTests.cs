// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.EventLog;

namespace EventLogExpert.Runtime.Tests.EventLog;

public sealed class XmlFilterMatchCacheTests
{
    private static readonly DateTime s_after = new(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime s_before = new(2024, 1, 2, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void GetMatch_WhenOnlyDateFilterDiffers_ReturnsNull()
    {
        XmlFilterMatchCache state = new();
        EventLogId logId = EventLogId.Create();
        Filter stored = XmlFilter(new DateFilter { After = s_after, Before = s_before });

        state.Set(stored, MatchFor(logId), state.NextSequence());

        Assert.NotNull(state.GetMatch(stored, logId));
        Assert.Null(state.GetMatch(XmlFilter(new DateFilter { After = s_after.AddHours(-1), Before = s_before }), logId));
    }

    [Fact]
    public void GetMatch_WhenUnsetOrCleared_ReturnsNullWithoutThrowing()
    {
        XmlFilterMatchCache state = new();
        EventLogId logId = EventLogId.Create();

        Assert.Null(state.GetMatch(XmlFilter(), logId));

        state.Set(XmlFilter(), MatchFor(logId), state.NextSequence());
        state.Clear();

        Assert.Null(state.GetMatch(XmlFilter(), logId));
    }

    [Fact]
    public void Remove_EvictsOnlyTheGivenLogsMatch()
    {
        XmlFilterMatchCache state = new();
        EventLogId kept = EventLogId.Create();
        EventLogId removed = EventLogId.Create();
        Filter filter = XmlFilter();

        state.Set(
            filter,
            new Dictionary<EventLogId, XmlFilterMatch>
            {
                [kept] = new XmlFilterMatch(kept, 0, 0, 0, []),
                [removed] = new XmlFilterMatch(removed, 0, 0, 0, [])
            },
            state.NextSequence());

        state.Remove(removed);

        Assert.NotNull(state.GetMatch(filter, kept));
        Assert.Null(state.GetMatch(filter, removed));
    }

    [Fact]
    public void Set_WhenSequenceIsHigherThanPublished_Applies()
    {
        XmlFilterMatchCache state = new();
        EventLogId logId = EventLogId.Create();
        Filter filter = XmlFilter();
        XmlFilterMatch first = new(logId, 0, 0, 1, [true]);
        XmlFilterMatch second = new(logId, 0, 0, 1, [false]);

        Assert.True(state.Set(filter, new Dictionary<EventLogId, XmlFilterMatch> { [logId] = first }, 5));
        Assert.True(state.Set(filter, new Dictionary<EventLogId, XmlFilterMatch> { [logId] = second }, 6));

        Assert.Same(second, state.GetMatch(filter, logId));
    }

    [Fact]
    public void Set_WhenSequenceIsLowerThanPublished_IsRejectedAndKeepsPriorMatch()
    {
        XmlFilterMatchCache state = new();
        EventLogId logId = EventLogId.Create();
        Filter filter = XmlFilter();
        XmlFilterMatch newer = new(logId, 0, 0, 1, [true]);
        XmlFilterMatch older = new(logId, 0, 0, 1, [false]);

        Assert.True(state.Set(filter, new Dictionary<EventLogId, XmlFilterMatch> { [logId] = newer }, 5));
        Assert.False(state.Set(filter, new Dictionary<EventLogId, XmlFilterMatch> { [logId] = older }, 3));

        Assert.Same(newer, state.GetMatch(filter, logId));
    }

    private static Dictionary<EventLogId, XmlFilterMatch> MatchFor(EventLogId logId) =>
        new() { [logId] = new XmlFilterMatch(logId, 0, 0, 0, []) };

    private static Filter XmlFilter(DateFilter? dateFilter = null) =>
        new(dateFilter, [SavedFilter.TryCreate("Xml.Contains(\"x\")") ?? throw new InvalidOperationException()]);
}
