// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.LogTable;
using Fluxor;
using NSubstitute;
using System.Collections.Immutable;
using CloseLogAction = EventLogExpert.Runtime.EventLog.CloseLogAction;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Runtime.Tests.EventLog;

public sealed class FilteringEffectsXmlMatchTests
{
    private const string ChannelLog = "ChannelLog";
    private const string FileLog = "FileLog";

    [Fact]
    public async Task ComputeFileMatch_WhenLogClosesDuringTheScan_DoesNotPublishTheClosedLogsMatch()
    {
        EventLogId logId = EventLogId.Create();
        Filter filter = XmlFilter();
        Harness harness = new();
        harness.SetState(filter, (logId, FileLog, LogPathType.File));
        harness.SetStore((logId, EmptyStore(0)));

        // The log leaves OpenLogs while the scan runs, so the compute must not republish its now-orphaned bitset.
        harness.Matcher.DuringCompute = () => harness.SetState(filter);

        await harness.Effects.HandleApplyFilter(new ApplyFilterAction(filter), harness.Dispatcher);

        Assert.Null(harness.MatchCache.GetMatch(filter, logId));
    }

    [Fact]
    public async Task ComputeFileMatch_WhenScanFaultsWhileFilterSuperseded_DoesNotReloadOrPublish()
    {
        EventLogId logId = EventLogId.Create();
        Filter superseding = XmlFilter("current");
        Filter faulting = XmlFilter("stale");
        Harness harness = new();

        // AppliedFilter has already advanced past the faulting apply, so the fault branch must not clear or reload.
        harness.SetState(superseding, (logId, FileLog, LogPathType.File));
        harness.SetStore((logId, EmptyStore(0)));
        harness.Matcher.ShouldThrow = true;

        await harness.Effects.HandleApplyFilter(new ApplyFilterAction(faulting), harness.Dispatcher);

        Assert.Null(harness.MatchCache.GetMatch(faulting, logId));
        harness.Dispatcher.DidNotReceive().Dispatch(Arg.Any<XmlFilterMatchReadyAction>());
        harness.Dispatcher.DidNotReceive().Dispatch(Arg.Any<CloseLogAction>());
    }

    [Fact]
    public async Task HandleApplyFilter_MixedScope_RendersFileMatchAndSkipsTheLoadedChannel()
    {
        EventLogId fileId = EventLogId.Create();
        EventLogId channelId = EventLogId.Create();
        Filter filter = XmlFilter();
        Harness harness = new();
        harness.SetState(filter, (fileId, FileLog, LogPathType.File), (channelId, ChannelLog, LogPathType.Channel));
        harness.SetStore((fileId, EmptyStore(0)), (channelId, EmptyStore(0)));

        // A reloaded Channel is already loaded with XML, so it routes to the columnar predicate and is not a reload target.
        harness.ConcurrencyState.MarkLoadedWithXml(channelId);

        await harness.Effects.HandleApplyFilter(new ApplyFilterAction(filter), harness.Dispatcher);

        Assert.Equal([FileLog], harness.Matcher.RenderedLogs);
        Assert.NotNull(harness.MatchCache.GetMatch(filter, fileId));
        Assert.Null(harness.MatchCache.GetMatch(filter, channelId));
        harness.Dispatcher.DidNotReceive().Dispatch(Arg.Any<CloseLogAction>());
    }

    [Fact]
    public async Task HandleApplyFilter_MixedScope_WhenFileComputeIsSupersededByRace_StillReloadsTheChannel()
    {
        EventLogId fileId = EventLogId.Create();
        EventLogId channelId = EventLogId.Create();
        Filter filter = XmlFilter();
        Harness harness = new();
        harness.SetState(filter, (fileId, FileLog, LogPathType.File), (channelId, ChannelLog, LogPathType.Channel));
        harness.SetStore((fileId, EmptyStore(0)), (channelId, EmptyStore(0)));

        // Simulate a racing same-filter recompute that already published a higher sequence, so this apply's File
        // compute loses the monotonic publish and returns Superseded. The Channel reload must still run.
        harness.MatchCache.Set(filter, new Dictionary<EventLogId, XmlFilterMatch>(), sequence: 1_000_000);

        await harness.Effects.HandleApplyFilter(new ApplyFilterAction(filter), harness.Dispatcher);

        harness.Dispatcher.Received().Dispatch(Arg.Is<CloseLogAction>(a => a != null && a.LogId == channelId));
        harness.Dispatcher.DidNotReceive().Dispatch(Arg.Is<CloseLogAction>(a => a != null && a.LogId == fileId));
    }

    [Fact]
    public async Task HandleApplyFilter_WhenANonXmlApplySupersedesAnInFlightScan_CancelsIt()
    {
        EventLogId logId = EventLogId.Create();
        Filter xml = XmlFilter();
        Filter nonXml = NonXmlFilter();
        Harness harness = new();
        harness.SetState(xml, (logId, FileLog, LogPathType.File));
        harness.SetStore((logId, EmptyStore(0)));

        // Mid-scan, a NEWER non-XML filter becomes authoritative; its no-compute apply must cancel the now-superseded scan.
        harness.Matcher.DuringCompute = () =>
        {
            harness.Matcher.DuringCompute = null;
            harness.SetState(nonXml, (logId, FileLog, LogPathType.File));
            harness.Effects.HandleApplyFilter(new ApplyFilterAction(nonXml), harness.Dispatcher).GetAwaiter().GetResult();
        };

        await harness.Effects.HandleApplyFilter(new ApplyFilterAction(xml), harness.Dispatcher);

        Assert.True(harness.Matcher.CapturedTokens[0].IsCancellationRequested);
    }

    [Fact]
    public async Task HandleApplyFilter_WhenAStaleNonXmlApplyRunsWhileAnXmlFilterIsCurrent_RetainsTheLiveMatch()
    {
        EventLogId logId = EventLogId.Create();
        Filter current = XmlFilter();
        Filter staleNonXml = NonXmlFilter();
        Harness harness = new();
        harness.SetState(current, (logId, FileLog, LogPathType.File));
        harness.SetStore((logId, EmptyStore(0)));

        await harness.Effects.HandleApplyFilter(new ApplyFilterAction(current), harness.Dispatcher);
        Assert.NotNull(harness.MatchCache.GetMatch(current, logId));

        // The non-XML apply is stale: `current` is still authoritative, so dropping the cache clear must leave its live
        // match intact (the pre-existing unconditional clear would have wiped it).
        await harness.Effects.HandleApplyFilter(new ApplyFilterAction(staleNonXml), harness.Dispatcher);

        Assert.NotNull(harness.MatchCache.GetMatch(current, logId));
    }

    [Fact]
    public async Task HandleApplyFilter_WhenAStaleNonXmlApplyRunsWhileTheCurrentXmlScanIsLive_DoesNotCancelIt()
    {
        EventLogId logId = EventLogId.Create();
        Filter current = XmlFilter();
        Filter staleNonXml = NonXmlFilter();
        Harness harness = new();
        harness.SetState(current, (logId, FileLog, LogPathType.File));
        harness.SetStore((logId, EmptyStore(0)));

        // A stale non-XML apply's effect runs LATE while `current` is still the authoritative AppliedFilter and its scan
        // is live (seeded here via DuringCompute). The dispatch-ordered guard must NOT cancel the current compute:
        // active.Filter == AppliedFilter AND OpenLogs is non-empty, so neither cancel condition holds. Seeding the live
        // compute is required - otherwise CancelSupersededScan no-ops and the test passes vacuously under the injection.
        harness.Matcher.DuringCompute = () =>
        {
            harness.Matcher.DuringCompute = null;
            harness.Effects.HandleApplyFilter(new ApplyFilterAction(staleNonXml), harness.Dispatcher).GetAwaiter().GetResult();
        };

        await harness.Effects.HandleApplyFilter(new ApplyFilterAction(current), harness.Dispatcher);

        Assert.False(harness.Matcher.CapturedTokens[0].IsCancellationRequested);
        Assert.NotNull(harness.MatchCache.GetMatch(current, logId));
    }

    [Fact]
    public async Task HandleApplyFilter_WhenFileNotLoadedWithXml_ComputesMatchAndPublishesReady()
    {
        EventLogId logId = EventLogId.Create();
        Filter filter = XmlFilter();
        Harness harness = new();
        harness.SetState(filter, (logId, FileLog, LogPathType.File));
        harness.SetStore((logId, EmptyStore(contentVersion: 0)));

        await harness.Effects.HandleApplyFilter(new ApplyFilterAction(filter), harness.Dispatcher);

        Assert.Equal([FileLog], harness.Matcher.RenderedLogs);
        Assert.NotNull(harness.MatchCache.GetMatch(filter, logId));
        harness.Dispatcher.Received(1).Dispatch(Arg.Any<XmlFilterMatchReadyAction>());
        harness.Dispatcher.DidNotReceive().Dispatch(Arg.Any<CloseLogAction>());
    }

    [Fact]
    public async Task HandleApplyFilter_WhenFilterDoesNotRequireXml_DoesNotScanOrReload()
    {
        EventLogId logId = EventLogId.Create();
        Filter filter = NonXmlFilter();
        Harness harness = new();
        harness.SetState(filter, (logId, FileLog, LogPathType.File));
        harness.SetStore((logId, EmptyStore(contentVersion: 0)));

        await harness.Effects.HandleApplyFilter(new ApplyFilterAction(filter), harness.Dispatcher);

        Assert.Empty(harness.Matcher.RenderedLogs);
        Assert.Null(harness.MatchCache.GetMatch(filter, logId));
        harness.Dispatcher.DidNotReceive().Dispatch(Arg.Any<XmlFilterMatchReadyAction>());
        harness.Dispatcher.DidNotReceive().Dispatch(Arg.Any<CloseLogAction>());
    }

    [Fact]
    public async Task HandleApplyFilter_WhenReappliedUnchanged_ReusesMatchWithoutRescanning()
    {
        EventLogId logId = EventLogId.Create();
        Filter filter = XmlFilter();
        Harness harness = new();
        harness.SetState(filter, (logId, FileLog, LogPathType.File));
        harness.SetStore((logId, EmptyStore(0)));

        await harness.Effects.HandleApplyFilter(new ApplyFilterAction(filter), harness.Dispatcher);
        await harness.Effects.HandleApplyFilter(new ApplyFilterAction(filter), harness.Dispatcher);

        Assert.Equal([FileLog], harness.Matcher.RenderedLogs);
        harness.Dispatcher.Received(1).Dispatch(Arg.Any<XmlFilterMatchReadyAction>());
    }

    [Fact]
    public async Task HandleApplyFilter_WhenSupersededByANewerFilter_CancelsThePriorScan()
    {
        EventLogId logId = EventLogId.Create();
        Filter first = XmlFilter("first");
        Filter second = XmlFilter("second");
        Harness harness = new();
        harness.SetState(first, (logId, FileLog, LogPathType.File));
        harness.SetStore((logId, EmptyStore(0)));

        harness.Matcher.DuringCompute = () =>
        {
            harness.Matcher.DuringCompute = null;
            harness.SetState(second, (logId, FileLog, LogPathType.File));
            harness.Effects.HandleApplyFilter(new ApplyFilterAction(second), harness.Dispatcher).GetAwaiter().GetResult();
        };

        await harness.Effects.HandleApplyFilter(new ApplyFilterAction(first), harness.Dispatcher);

        Assert.True(harness.Matcher.CapturedTokens[0].IsCancellationRequested);
        Assert.Null(harness.MatchCache.GetMatch(first, logId));
    }

    [Fact]
    public async Task HandleCloseAllLogs_RetainsInertMatchInsteadOfEagerlyClearing()
    {
        EventLogId logId = EventLogId.Create();
        Filter filter = XmlFilter();
        Harness harness = new();
        harness.SetState(filter, (logId, FileLog, LogPathType.File));
        harness.SetStore((logId, EmptyStore(0)));

        await harness.Effects.HandleApplyFilter(new ApplyFilterAction(filter), harness.Dispatcher);
        Assert.NotNull(harness.MatchCache.GetMatch(filter, logId));

        await harness.Effects.HandleCloseAllLogs(harness.Dispatcher);

        // Close-all no longer eagerly clears the cache (an eager clear would race a re-added log's live match). The
        // filter-stamped match is retained inert - the gate short-circuits on empty OpenLogs - and is reclaimed by the
        // next XML publish's wholesale Set or a reopen, never wiping a live match mid-flight.
        Assert.NotNull(harness.MatchCache.GetMatch(filter, logId));
    }

    [Fact]
    public async Task HandleCloseAllLogs_WhileAScanIsInFlight_CancelsIt()
    {
        EventLogId logId = EventLogId.Create();
        Filter xml = XmlFilter();
        Harness harness = new();
        harness.SetState(xml, (logId, FileLog, LogPathType.File));
        harness.SetStore((logId, EmptyStore(0)));

        // Mid-scan, every log closes; the scan's result is dead (IsDeferred short-circuits on empty OpenLogs) so close-all
        // must cancel it.
        harness.Matcher.DuringCompute = () =>
        {
            harness.Matcher.DuringCompute = null;
            harness.SetState(xml);
            harness.Effects.HandleCloseAllLogs(harness.Dispatcher).GetAwaiter().GetResult();
        };

        await harness.Effects.HandleApplyFilter(new ApplyFilterAction(xml), harness.Dispatcher);

        Assert.True(harness.Matcher.CapturedTokens[0].IsCancellationRequested);
    }

    [Fact]
    public async Task HandleCloseLog_EvictsTheClosedLogsMatch()
    {
        EventLogId logId = EventLogId.Create();
        Filter filter = XmlFilter();
        Harness harness = new();
        harness.SetState(filter, (logId, FileLog, LogPathType.File));
        harness.SetStore((logId, EmptyStore(0)));

        await harness.Effects.HandleApplyFilter(new ApplyFilterAction(filter), harness.Dispatcher);
        Assert.NotNull(harness.MatchCache.GetMatch(filter, logId));

        await harness.Effects.HandleCloseLog(new CloseLogAction(logId, FileLog), harness.Dispatcher);

        Assert.Null(harness.MatchCache.GetMatch(filter, logId));
    }

    [Fact]
    public async Task HandleLoadEvents_AfterTheStoreGrows_RecomputesMatchForTheNewStamp()
    {
        EventLogId logId = EventLogId.Create();
        Filter filter = XmlFilter();
        Harness harness = new();
        harness.SetState(filter, (logId, FileLog, LogPathType.File));
        harness.SetStore((logId, EmptyStore(contentVersion: 0)));

        await harness.Effects.HandleApplyFilter(new ApplyFilterAction(filter), harness.Dispatcher);

        harness.SetStore((logId, EmptyStore(contentVersion: 1)));

        await harness.Effects.HandleLoadEvents(
            new LoadEventsAction(new EventLogData(FileLog, LogPathType.File) { Id = logId }, []), harness.Dispatcher);

        Assert.Equal([FileLog, FileLog], harness.Matcher.RenderedLogs);
        Assert.Equal(1, harness.MatchCache.GetMatch(filter, logId)!.ContentVersion);
        harness.Dispatcher.Received(2).Dispatch(Arg.Any<XmlFilterMatchReadyAction>());
    }

    [Fact]
    public async Task HandleLoadEvents_WhenRecomputeSupersedesTheScan_CancelsItWithoutReloading()
    {
        EventLogId logId = EventLogId.Create();
        Filter filter = XmlFilter();
        Harness harness = new();
        harness.SetState(filter, (logId, FileLog, LogPathType.File));
        harness.SetStore((logId, EmptyStore(contentVersion: 0)));

        harness.Matcher.DuringCompute = () =>
        {
            harness.Matcher.DuringCompute = null;
            harness.SetStore((logId, EmptyStore(contentVersion: 1)));
            harness.Effects
                .HandleLoadEvents(
                    new LoadEventsAction(new EventLogData(FileLog, LogPathType.File) { Id = logId }, []),
                    harness.Dispatcher)
                .GetAwaiter()
                .GetResult();
        };

        await harness.Effects.HandleApplyFilter(new ApplyFilterAction(filter), harness.Dispatcher);

        Assert.True(harness.Matcher.CapturedTokens[0].IsCancellationRequested);
        harness.Dispatcher.DidNotReceive().Dispatch(Arg.Any<CloseLogAction>());
        Assert.Equal(1, harness.MatchCache.GetMatch(filter, logId)!.ContentVersion);
    }

    private static EventColumnStore EmptyStore(long contentVersion) =>
        EventColumnStore.Build([], generation: 0, contentVersion);

    private static Filter NonXmlFilter() =>
        new(null, [SavedFilter.TryCreate("Level == \"Error\"") ?? throw new InvalidOperationException()]);

    private static Filter XmlFilter(string needle = "x") =>
        new(null, [SavedFilter.TryCreate($"Xml.Contains(\"{needle}\")") ?? throw new InvalidOperationException()]);

    private sealed class FakeMatcher : IXmlFilterMatcher
    {
        public List<CancellationToken> CapturedTokens { get; } = [];

        public Action? DuringCompute { get; set; }

        public List<string> RenderedLogs { get; } = [];

        public bool ShouldThrow { get; set; }

        public XmlFilterMatch ComputeMatch(
            IEventColumnReader reader,
            Filter filter,
            string owningLog,
            LogPathType pathType,
            CancellationToken cancellationToken)
        {
            RenderedLogs.Add(owningLog);
            CapturedTokens.Add(cancellationToken);

            if (ShouldThrow) { throw new InvalidOperationException("scan faulted"); }

            DuringCompute?.Invoke();

            cancellationToken.ThrowIfCancellationRequested();

            return new XmlFilterMatch(
                reader.LogId, reader.Generation, reader.ContentVersion, reader.Count, new bool[reader.Count]);
        }
    }

    private sealed class Harness
    {
        private readonly LogCloseCoordinator _closeCoordinator = new();
        private RawEventStoreState _rawStore = new();

        private EventLogState _state = new();

        public Harness()
        {
            var eventLogState = Substitute.For<IState<EventLogState>>();
            eventLogState.Value.Returns(_ => _state);

            var rawEventStore = Substitute.For<IState<RawEventStoreState>>();
            rawEventStore.Value.Returns(_ => _rawStore);

            var logger = Substitute.For<ITraceLogger>();

            // A dispatched CloseLog completes the reload coordinator's close-wait so a Channel reload does not block
            // on the 30-second production close timeout.
            Dispatcher.When(d => d.Dispatch(Arg.Any<CloseLogAction>()))
                .Do(callInfo =>
                {
                    if (callInfo.Arg<CloseLogAction>() is { } close) { _closeCoordinator.CompleteCloseFor(close.LogId); }
                });

            Effects = new FilteringEffects(
                eventLogState,
                rawEventStore,
                new LiveTailIngestCoordinator(Dispatcher, Timeout.InfiniteTimeSpan),
                new XmlReloadCoordinator(eventLogState, _closeCoordinator, ConcurrencyState, logger),
                Matcher,
                MatchCache,
                ConcurrencyState,
                logger);
        }

        public EventLogConcurrencyState ConcurrencyState { get; } = new();

        public IDispatcher Dispatcher { get; } = Substitute.For<IDispatcher>();

        public FilteringEffects Effects { get; }

        public XmlFilterMatchCache MatchCache { get; } = new();

        public FakeMatcher Matcher { get; } = new();

        public void SetState(Filter appliedFilter, params (EventLogId Id, string Name, LogPathType Type)[] logs)
        {
            ImmutableDictionary<string, OpenLogInfo>.Builder builder =
                ImmutableDictionary.CreateBuilder<string, OpenLogInfo>(StringComparer.Ordinal);

            foreach ((EventLogId id, string name, LogPathType type) in logs) { builder[name] = new OpenLogInfo(id, type); }

            _state = new EventLogState { OpenLogs = builder.ToImmutable(), AppliedFilter = appliedFilter };
        }

        public void SetStore(params (EventLogId Id, EventColumnStore Store)[] stores)
        {
            ImmutableDictionary<EventLogId, EventColumnStore>.Builder builder =
                ImmutableDictionary.CreateBuilder<EventLogId, EventColumnStore>();

            foreach ((EventLogId id, EventColumnStore store) in stores) { builder[id] = store; }

            _rawStore = new RawEventStoreState { ByLog = builder.ToImmutable() };
        }
    }
}
