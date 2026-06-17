// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.LogTable;
using Fluxor;
using NSubstitute;

namespace EventLogExpert.Runtime.Tests.EventLog;

public sealed class EventLogQueriesTests
{
    private static readonly DateTime s_fallbackNow = new(2024, 6, 15, 9, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void GetEventDateRange_ReadsRawStore_ReturnsHourAlignedBounds()
    {
        var (state, id) = Opened("Populated");
        state = Ingest(state, id, RawIngestMode.Append,
            Ev(1, new DateTime(2024, 1, 1, 14, 45, 30, DateTimeKind.Utc)),
            Ev(2, new DateTime(2024, 1, 1, 8, 15, 10, DateTimeKind.Utc)));
        var queries = new EventLogQueries(StateReturning(state));

        var (after, before) = queries.GetEventDateRange(s_fallbackNow);

        Assert.Equal(new DateTime(2024, 1, 1, 8, 0, 0, DateTimeKind.Utc), after);
        Assert.Equal(new DateTime(2024, 1, 1, 15, 0, 0, DateTimeKind.Utc), before);
    }

    [Fact]
    public void GetEventDateRange_WhenNoEvents_PassesFallbackThrough()
    {
        var queries = new EventLogQueries(StateReturning(new RawEventStoreState()));

        var (after, before) = queries.GetEventDateRange(s_fallbackNow);

        Assert.Equal(new DateTime(2024, 6, 15, 9, 0, 0, DateTimeKind.Utc), after);
        Assert.Equal(new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Utc), before);
    }

    [Fact]
    public void GetPropertyValues_LevelField_ReturnsAllSeverityLevels()
    {
        var queries = new EventLogQueries(StateReturning(new RawEventStoreState()));

        Assert.Equal(Enum.GetNames<SeverityLevel>(), queries.GetPropertyValues(EventProperty.Level));
    }

    [Fact]
    public void GetPropertyValues_ReturnsDistinctSortedSourcesAcrossEvents()
    {
        var (state, id) = Opened("Populated");
        state = Ingest(state, id, RawIngestMode.Append,
            Ev(2, source: "Bravo"), Ev(1, source: "Alpha"), Ev(3, source: "Alpha"));
        var queries = new EventLogQueries(StateReturning(state));

        var sources = queries.GetPropertyValues(EventProperty.Source);

        Assert.Equal(["Alpha", "Bravo"], sources);
    }

    private static ResolvedEvent Ev(int id, DateTime timeCreated = default, string source = "TestSource") =>
        new("Populated", LogPathType.Channel)
        {
            Id = id,
            Source = source,
            TimeCreated = timeCreated
        };

    private static RawEventStoreState Ingest(
        RawEventStoreState state,
        EventLogId id,
        RawIngestMode mode,
        params ResolvedEvent[] events) =>
        RawEventStoreReducers.ReduceIngestRawEvents(
            state,
            new IngestRawEventsAction(
                new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>> { [id] = events },
                mode));

    private static (RawEventStoreState State, EventLogId Id) Opened(string name)
    {
        var logData = new EventLogData(name, LogPathType.Channel, []);
        var state = RawEventStoreReducers.ReduceAddTable(new RawEventStoreState(), new AddTableAction(logData));

        return (state, logData.Id);
    }

    private static IState<RawEventStoreState> StateReturning(RawEventStoreState state)
    {
        var stateMock = Substitute.For<IState<RawEventStoreState>>();
        stateMock.Value.Returns(state);

        return stateMock;
    }
}
