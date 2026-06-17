// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.TestUtils;
using EventLogExpert.Runtime.EventLog;
using Fluxor;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Tests.EventLog;

public sealed class EventLogQueriesTests
{
    private static readonly DateTime s_fallbackNow = new(2024, 6, 15, 9, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void GetEventDateRange_ReadsActiveLogsFromState_ReturnsHourAlignedBounds()
    {
        var log = CreateLog(
            new DateTime(2024, 1, 1, 14, 45, 30, DateTimeKind.Utc),
            new DateTime(2024, 1, 1, 8, 15, 10, DateTimeKind.Utc));
        var state = new EventLogState
        {
            ActiveLogs = ImmutableDictionary<string, EventLogData>.Empty.Add("Populated", log)
        };
        var queries = new EventLogQueries(StateReturning(state));

        var (after, before) = queries.GetEventDateRange(s_fallbackNow);

        Assert.Equal(new DateTime(2024, 1, 1, 8, 0, 0, DateTimeKind.Utc), after);
        Assert.Equal(new DateTime(2024, 1, 1, 15, 0, 0, DateTimeKind.Utc), before);
    }

    [Fact]
    public void GetEventDateRange_WhenNoActiveLogs_PassesFallbackThrough()
    {
        var queries = new EventLogQueries(StateReturning(new EventLogState()));

        var (after, before) = queries.GetEventDateRange(s_fallbackNow);

        Assert.Equal(new DateTime(2024, 6, 15, 9, 0, 0, DateTimeKind.Utc), after);
        Assert.Equal(new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Utc), before);
    }

    private static EventLogData CreateLog(DateTime newest, DateTime oldest) =>
        new("Populated", LogPathType.Channel, new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(timeCreated: newest),
            FilterEventBuilder.CreateTestEvent(timeCreated: oldest)
        });

    private static IState<EventLogState> StateReturning(EventLogState state)
    {
        var stateMock = Substitute.For<IState<EventLogState>>();
        stateMock.Value.Returns(state);

        return stateMock;
    }
}
