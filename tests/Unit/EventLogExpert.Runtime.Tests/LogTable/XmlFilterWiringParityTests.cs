// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Compilation;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.LogTable.OrderedView;
using EventLogExpert.Runtime.Tests.LogTable.TestSupport;
using EventLogExpert.Runtime.Tests.TestUtils;
using Fluxor;
using NSubstitute;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using CloseLogAction = EventLogExpert.Runtime.EventLog.CloseLogAction;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class XmlFilterWiringParityTests
{
    private const string LogName = "FileLog";

    private static readonly DateTime s_baseTime = new(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    public static TheoryData<string, string, bool> FilterShapes => new()
    {
        { "cheap-and-xml", "Level == \"Error\" && Xml.Contains(\"needle\")", false },
        { "pure-xml", "Xml.Contains(\"needle\")", false },
        { "cheap-or-xml", "Level == \"Error\" || Xml.Contains(\"needle\")", false },
        { "xml-exclude", "Xml.Contains(\"needle\")", true },
        { "source-and-xml", "Source == \"S1\" && Xml.Contains(\"needle\")", false }
    };

    [Theory]
    [MemberData(nameof(FilterShapes))]
    public async Task OnDemandXmlFilter_RendersTheSameVisibleRowsAndPresenceAsTheColumnarReload(
        string shape,
        string expression,
        bool isExcluded)
    {
        _ = shape;
        EventLogId logId = EventLogId.Create();
        Filter filter = FilterOf(expression, isExcluded);

        EventColumnStore storeWithXml = EventColumnStore.Build(Events(withXml: true), generation: 0, contentVersion: 0);
        EventColumnStore storeNoXml = EventColumnStore.Build(Events(withXml: false), generation: 0, contentVersion: 0);
        bool[] oracle = OracleBitset(storeWithXml, logId, filter);

        Assert.InRange(oracle.Count(matches => matches), 1, oracle.Length - 1);

        IReadOnlyList<EventLocator> referenceView;
        FilteredLogPresence referencePresence;

        await using (XmlFilterWiringParityHarness columnar = new())
        {
            (referenceView, referencePresence) = await columnar.RunColumnarReloadAsync(filter, logId, storeWithXml);
        }

        IReadOnlyList<EventLocator> onDemandView;
        FilteredLogPresence onDemandPresence;

        await using (XmlFilterWiringParityHarness gated = new())
        {
            (onDemandView, onDemandPresence) = await gated.RunOnDemandAsync(filter, logId, storeNoXml, oracle);
        }

        Assert.NotEmpty(referenceView);
        Assert.Equal(referenceView, onDemandView);

        Assert.NotEqual(FilteredLogPresence.Pending, referencePresence);
        Assert.NotEqual(FilteredLogPresence.Pending, onDemandPresence);
        Assert.Equal(referencePresence, onDemandPresence);
    }

    private static IReadOnlyList<ResolvedEvent> Events(bool withXml) =>
    [
        Row(1, "Error", "S1", needle: true, withXml),
        Row(2, "Warning", "S1", needle: true, withXml),
        Row(3, "Error", "S2", needle: false, withXml),
        Row(4, "Information", "S2", needle: true, withXml),
        Row(5, "Warning", "S1", needle: false, withXml)
    ];

    private static Filter FilterOf(string expression, bool isExcluded)
    {
        SavedFilter saved = SavedFilter.TryCreate(expression, isExcluded: isExcluded) ??
            throw new InvalidOperationException($"Failed to compile '{expression}'.");

        return new Filter(null, [saved]);
    }

    private static bool[] OracleBitset(EventColumnStore storeWithXml, EventLogId logId, Filter filter)
    {
        Func<IEventColumnReader, EventLocator, bool> survives = FilterService.CompileSurvivorPredicate(filter);
        IEventColumnReader reader = storeWithXml.CreateReader(logId);
        bool[] bits = new bool[reader.Count];

        for (int index = 0; index < bits.Length; index++)
        {
            bits[index] = survives(reader, reader.LocatorAt(index));
        }

        return bits;
    }

    private static ResolvedEvent Row(long recordId, string level, string source, bool needle, bool withXml) =>
        new(LogName, LogPathType.File)
        {
            RecordId = recordId,
            TimeCreated = s_baseTime.AddSeconds(recordId),
            Id = 1000,
            Level = level,
            Source = source,
            LogName = LogName,
            Xml = withXml ? needle ? "<Event>needle</Event>" : "<Event>none</Event>" : string.Empty
        };

    private sealed class FakeXmlFilterMatcher : IXmlFilterMatcher
    {
        public bool[]? Bitset { get; set; }

        public XmlFilterMatch ComputeMatch(
            IEventColumnReader reader,
            Filter filter,
            string owningLog,
            LogPathType pathType,
            CancellationToken cancellationToken)
        {
            bool[] bits = Bitset ?? new bool[reader.Count];

            return new XmlFilterMatch(reader.LogId, reader.Generation, reader.ContentVersion, reader.Count, bits);
        }
    }

    private sealed class XmlFilterWiringParityHarness : IAsyncDisposable
    {
        private readonly LogCloseCoordinator _closeCoordinator = new();
        private readonly ConcurrentQueue<object> _dispatched = new();
        private readonly LiveTailIngestCoordinator _liveTailCoordinator;
        private readonly FilteredLogPresenceCoordinator _presenceCoordinator;

        private EventLogState _eventLog = new();
        private LogTableState _logTable = new();
        private FilteredLogPresenceState _presence = new();
        private RawEventStoreState _rawStore = new();

        public XmlFilterWiringParityHarness()
        {
            var eventLogState = Substitute.For<IState<EventLogState>>();
            eventLogState.Value.Returns(_ => _eventLog);

            var rawEventStore = Substitute.For<IState<RawEventStoreState>>();
            rawEventStore.Value.Returns(_ => _rawStore);

            var logTableState = Substitute.For<IState<LogTableState>>();
            logTableState.Value.Returns(_ => _logTable);

            var presenceState = Substitute.For<IState<FilteredLogPresenceState>>();
            presenceState.Value.Returns(_ => _presence);

            var logger = Substitute.For<ITraceLogger>();

            Dispatcher.When(dispatcher => dispatcher.Dispatch(Arg.Any<object>()))
                .Do(callInfo =>
                {
                    object? action = callInfo.Arg<object>();

                    if (action is null) { return; }

                    if (action is CloseLogAction close) { _closeCoordinator.CompleteCloseFor(close.LogId); }

                    _dispatched.Enqueue(action);
                });

            _liveTailCoordinator = new LiveTailIngestCoordinator(Dispatcher, Timeout.InfiniteTimeSpan);

            Filtering = new FilteringEffects(
                eventLogState,
                rawEventStore,
                _liveTailCoordinator,
                new XmlReloadCoordinator(eventLogState, _closeCoordinator, ConcurrencyState, logger),
                Matcher,
                MatchCache,
                ConcurrencyState,
                new ImmediateCpuWorkScheduler(),
                logger);

            Bridge = new OrderedViewDispatchBridge(Dispatcher, Writer);

            OrderedView = new OrderedViewShadowEffects(
                eventLogState,
                logTableState,
                rawEventStore,
                Writer,
                Issuer,
                Bridge,
                Dispatcher,
                ConcurrencyState,
                MatchCache);

            _presenceCoordinator = new FilteredLogPresenceCoordinator(
                Dispatcher, eventLogState, rawEventStore, presenceState, ConcurrencyState, MatchCache, scanInline: true);

            Presence = new FilteredLogPresenceEffects(_presenceCoordinator);
        }

        public OrderedViewDispatchBridge Bridge { get; }

        public EventLogConcurrencyState ConcurrencyState { get; } = new();

        public IDispatcher Dispatcher { get; } = Substitute.For<IDispatcher>();

        public FilteringEffects Filtering { get; }

        public ViewRequestIssuer Issuer { get; } = new();

        public XmlFilterMatchCache MatchCache { get; } = new();

        public FakeXmlFilterMatcher Matcher { get; } = new();

        public OrderedViewShadowEffects OrderedView { get; }

        public FilteredLogPresenceEffects Presence { get; }

        public OrderedViewWriter Writer { get; } = new(publishIntervalMs: 0);

        public async ValueTask DisposeAsync()
        {
            _presenceCoordinator.Dispose();
            _liveTailCoordinator.Dispose();
            Bridge.Dispose();
            await Writer.DisposeAsync();
        }

        public async Task<(IReadOnlyList<EventLocator> View, FilteredLogPresence Presence)> RunColumnarReloadAsync(
            Filter filter,
            EventLogId logId,
            EventColumnStore storeWithXml)
        {
            ConcurrencyState.MarkLoadedWithXml(logId);
            SetState(logId, filter, storeWithXml);

            await OrderedView.HandleApplyFilter(new ApplyFilterAction(filter), Dispatcher);
            await Presence.HandleApplyFilter(Dispatcher);
            await DrainAsync();

            Assert.Null(Issuer.LastFault);

            return (await SnapshotLocatorsAsync(), _presence.ByLog[logId]);
        }

        public async Task<(IReadOnlyList<EventLocator> View, FilteredLogPresence Presence)> RunOnDemandAsync(
            Filter filter,
            EventLogId logId,
            EventColumnStore storeNoXml,
            bool[] oracleBitset)
        {
            Matcher.Bitset = oracleBitset;
            SetState(logId, filter, storeNoXml);

            await OrderedView.HandleApplyFilter(new ApplyFilterAction(filter), Dispatcher);
            await Presence.HandleApplyFilter(Dispatcher);
            await DrainAsync();

            Dispatcher.DidNotReceive().Dispatch(Arg.Any<ViewRequestInvalidatedAction>());
            Assert.Equal(FilteredLogPresence.Pending, _presence.ByLog[logId]);

            await Filtering.HandleApplyFilter(new ApplyFilterAction(filter), Dispatcher);
            await DrainAsync();

            Dispatcher.Received().Dispatch(Arg.Any<XmlFilterMatchReadyAction>());
            Assert.Null(Issuer.LastFault);

            return (await SnapshotLocatorsAsync(), _presence.ByLog[logId]);
        }

        private async Task DrainAsync()
        {
            while (_dispatched.TryDequeue(out object? action))
            {
                switch (action)
                {
                    case FilteredPresenceInvalidatedAction invalidated:
                        _presence = FilteredLogPresenceReducers.ReduceInvalidated(_presence, invalidated);

                        break;
                    case FilteredPresenceUpdatedAction updated:
                        _presence = FilteredLogPresenceReducers.ReduceUpdated(_presence, updated);

                        break;
                    case XmlFilterMatchReadyAction:
                        await OrderedView.HandleXmlFilterMatchReady(Dispatcher);
                        await Presence.HandleXmlFilterMatchReady(Dispatcher);

                        break;
                }
            }
        }

        private void SetState(EventLogId logId, Filter filter, EventColumnStore store)
        {
            _logTable = new LogTableState
            {
                ActiveEventLogId = logId,
                EventTables = [new LogView(logId)],
                IsDescending = true,
                RequestedIsDescending = true,
                AppliedFilter = filter
            };

            _rawStore = new RawEventStoreState
            {
                ByLog = ImmutableDictionary<EventLogId, EventColumnStore>.Empty.Add(logId, store)
            };

            _eventLog = new EventLogState
            {
                OpenLogs = ImmutableDictionary<string, OpenLogInfo>.Empty.Add(LogName, new OpenLogInfo(logId, LogPathType.File)),
                AppliedFilter = filter
            };

            _presence = new FilteredLogPresenceState
            {
                ByLog = ImmutableDictionary<EventLogId, FilteredLogPresence>.Empty.Add(logId, FilteredLogPresence.Pending)
            };
        }

        private async Task<IReadOnlyList<EventLocator>> SnapshotLocatorsAsync()
        {
            await Writer.DrainAsync().WaitAsync(OrderedViewTestTimeouts.Default);
            OrderedViewSnapshot snapshot = await Writer.DrainAsync().WaitAsync(OrderedViewTestTimeouts.Default);

            List<EventLocator> locators = new(snapshot.Count);

            for (int index = 0; index < snapshot.Count; index++)
            {
                locators.Add(snapshot.At(index).Locator);
            }

            return locators;
        }
    }
}
