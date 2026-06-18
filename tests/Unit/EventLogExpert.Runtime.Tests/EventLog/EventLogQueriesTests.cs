// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.LogTable;
using Fluxor;
using NSubstitute;
using Reducers = EventLogExpert.Runtime.EventLog.Reducers;

namespace EventLogExpert.Runtime.Tests.EventLog;

public sealed class EventLogQueriesTests
{
    private static readonly DateTime s_fallbackNow = new(2024, 6, 15, 9, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void GetChannelNames_ReturnsChannelNamesOnly_ExcludesFileLogs()
    {
        var state = Reducers.ReduceOpenLog(
            new EventLogState(),
            new OpenLogAction("Application", LogPathType.Channel));
        state = Reducers.ReduceOpenLog(
            state,
            new OpenLogAction("System", LogPathType.Channel));
        state = Reducers.ReduceOpenLog(
            state,
            new OpenLogAction(@"C:\logs\Sample.evtx", LogPathType.File));

        var queries = new EventLogQueries(
            StateReturning(new RawEventStoreState()),
            EventLogStateReturning(state));

        var names = queries.GetChannelNames();

        Assert.Equal(
            ["Application", "System"],
            names.OrderBy(name => name, StringComparer.Ordinal));
        Assert.DoesNotContain(@"C:\logs\Sample.evtx", names);
    }

    [Fact]
    public void GetEventDateRange_ReadsRawStore_ReturnsHourAlignedBounds()
    {
        var (state, id) = Opened("Populated");
        state = Ingest(state, id, RawIngestMode.Append,
            Ev(1, new DateTime(2024, 1, 1, 14, 45, 30, DateTimeKind.Utc)),
            Ev(2, new DateTime(2024, 1, 1, 8, 15, 10, DateTimeKind.Utc)));
        var queries = new EventLogQueries(StateReturning(state), EventLogStateReturning(new EventLogState()));

        var (after, before) = queries.GetEventDateRange(s_fallbackNow);

        Assert.Equal(new DateTime(2024, 1, 1, 8, 0, 0, DateTimeKind.Utc), after);
        Assert.Equal(new DateTime(2024, 1, 1, 15, 0, 0, DateTimeKind.Utc), before);
    }

    [Fact]
    public void GetEventDateRange_WhenNoEvents_PassesFallbackThrough()
    {
        var queries = new EventLogQueries(
            StateReturning(new RawEventStoreState()),
            EventLogStateReturning(new EventLogState()));

        var (after, before) = queries.GetEventDateRange(s_fallbackNow);

        Assert.Equal(new DateTime(2024, 6, 15, 9, 0, 0, DateTimeKind.Utc), after);
        Assert.Equal(new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Utc), before);
    }

    [Fact]
    public void GetPropertyValues_LevelField_ReturnsAllSeverityLevels()
    {
        var queries = new EventLogQueries(
            StateReturning(new RawEventStoreState()),
            EventLogStateReturning(new EventLogState()));

        Assert.Equal(Enum.GetNames<SeverityLevel>(), queries.GetPropertyValues(EventProperty.Level));
    }

    [Fact]
    public void GetPropertyValues_ReturnsDistinctSortedSourcesAcrossEvents()
    {
        var (state, id) = Opened("Populated");
        state = Ingest(state, id, RawIngestMode.Append,
            Ev(2, source: "Bravo"), Ev(1, source: "Alpha"), Ev(3, source: "Alpha"));
        var queries = new EventLogQueries(StateReturning(state), EventLogStateReturning(new EventLogState()));

        var sources = queries.GetPropertyValues(EventProperty.Source);

        Assert.Equal(["Alpha", "Bravo"], sources);
    }

    [Fact]
    public void RoundOrFallback_WhenNewestIsExactHour_DoesNotPushBeforeForward()
    {
        var exactHour = new DateTime(2024, 1, 1, 14, 0, 0, DateTimeKind.Utc);
        (DateTime After, DateTime Before)? range = (exactHour, exactHour);

        var (after, before) = range.RoundOrFallback(s_fallbackNow);

        Assert.Equal(exactHour, after);
        Assert.Equal(exactHour, before);
    }

    [Fact]
    public void RoundOrFallback_WhenOldestIsExactHour_DoesNotPushAfterBackward()
    {
        var exactHour = new DateTime(2024, 1, 1, 8, 0, 0, DateTimeKind.Utc);
        var newer = new DateTime(2024, 1, 1, 9, 30, 0, DateTimeKind.Utc);
        (DateTime After, DateTime Before)? range = (exactHour, newer);

        var (after, _) = range.RoundOrFallback(s_fallbackNow);

        Assert.Equal(exactHour, after);
    }

    private static ResolvedEvent Ev(int id, DateTime timeCreated = default, string source = "TestSource") =>
        new("Populated", LogPathType.Channel)
        {
            Id = id,
            Source = source,
            TimeCreated = timeCreated
        };

    private static IState<EventLogState> EventLogStateReturning(EventLogState state)
    {
        var stateMock = Substitute.For<IState<EventLogState>>();
        stateMock.Value.Returns(state);

        return stateMock;
    }

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
        var logData = new EventLogData(name, LogPathType.Channel);
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
