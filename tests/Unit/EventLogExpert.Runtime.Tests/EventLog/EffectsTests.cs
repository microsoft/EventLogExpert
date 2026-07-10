// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.Resolvers;
using EventLogExpert.Filtering.Compilation;
using EventLogExpert.Filtering.TestUtils;
using EventLogExpert.Filtering.TestUtils.Constants;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterProgress;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.StatusBar;
using EventLogExpert.Runtime.Tests.TestUtils.Constants;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Collections.Immutable;
using CloseLogAction = EventLogExpert.Runtime.EventLog.CloseLogAction;

namespace EventLogExpert.Runtime.Tests.EventLog;

public sealed class EffectsTests
{
    [Fact]
    public async Task HandleAddEvent_WhenBufferReachesMaxEvents_ShouldSetFullFlag()
    {
        // Arrange
        var existingBuffer = Enumerable.Range(0, EventLogState.MaxNewEvents - 1)
            .Select(i => FilterEventBuilder.CreateTestEvent(i, logName: Constants.LogNameTestLog))
            .ToList();

        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher) = CreateEffects(false, activeLogs, existingBuffer);

        var newEvent = FilterEventBuilder.CreateTestEvent(1000, logName: Constants.LogNameTestLog);
        var action = new AddEventAction(newEvent);

        // Act
        await effects.HandleAddEvent(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1)
            .Dispatch(Arg.Is<EventBufferedAction>(a =>
                a.IsFull == true && a.UpdatedBuffer.Count == EventLogState.MaxNewEvents));
    }

    [Fact]
    public async Task HandleAddEvent_WhenContinuouslyUpdateFalse_ShouldBufferEvent()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher) = CreateEffects(
            false,
            activeLogs);

        var newEvent = FilterEventBuilder.CreateTestEvent(100, logName: Constants.LogNameTestLog);
        var action = new AddEventAction(newEvent);

        // Act
        await effects.HandleAddEvent(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1)
            .Dispatch(Arg.Is<EventBufferedAction>(a =>
                a.UpdatedBuffer.Count == 1 && a.UpdatedBuffer[0] == newEvent));
    }

    [Fact]
    public async Task HandleAddEvent_WhenContinuouslyUpdateTrue_AndEventFilteredOut_ShouldNotDispatchAppend()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher, _, _, mockFilterService) =
            CreateEffectsWithServices(true, activeLogs);

        mockFilterService.GetFilteredEvents(Arg.Any<IEnumerable<ResolvedEvent>>(), Arg.Any<Filter>())
            .Returns(new List<ResolvedEvent>());

        var newEvent = FilterEventBuilder.CreateTestEvent(100, logName: Constants.LogNameTestLog);
        var action = new AddEventAction(newEvent);

        // Act
        await effects.HandleAddEvent(action, mockDispatcher);

        // Assert
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<AppendTableEventsAction>());
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<DisplayReadyAction>());
    }

    [Fact]
    public async Task HandleAddEvent_WhenContinuouslyUpdateTrue_AndEventFilteredOut_ShouldStillIngestRaw()
    {
        // Arrange: a live-tail event the active filter hides still belongs in the raw store.
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher, _, _, mockFilterService) =
            CreateEffectsWithServices(true, activeLogs);

        mockFilterService.GetFilteredEvents(Arg.Any<IEnumerable<ResolvedEvent>>(), Arg.Any<Filter>())
            .Returns(new List<ResolvedEvent>());

        var newEvent = FilterEventBuilder.CreateTestEvent(100, logName: Constants.LogNameTestLog);

        // Act
        await effects.HandleAddEvent(new AddEventAction(newEvent), mockDispatcher);

        // Assert: raw is ingested unconditionally (Prepend) even though the filtered display append is skipped.
        mockDispatcher.Received(1).Dispatch(Arg.Is<IngestRawEventsAction>(a =>
            a.Mode == RawIngestMode.Prepend && a.EventsByLog.ContainsKey(logData.Id)));
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<AppendTableEventsAction>());
    }

    [Fact]
    public async Task HandleAddEvent_WhenContinuouslyUpdateTrue_ShouldIngestRawAndAppend()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);

        var state = new EventLogState
        {
            ContinuouslyUpdate = true,
            OpenLogs = ImmutableDictionary<string, OpenLogInfo>.Empty
                .Add(Constants.LogNameTestLog, new OpenLogInfo(logData.Id, LogPathType.Channel)),
            NewEventBuffer = [],
            AppliedFilter = new Filter(null, [])
        };

        var rawState = new RawEventStoreState
        {
            ByLog = ImmutableDictionary<EventLogId, EventColumnStore>.Empty.Add(logData.Id, EventColumnStore.Empty)
        };

        var (effects, mockDispatcher, _) = CreateEffectsWithMutableState(() => state, () => rawState);

        // Fluxor runs the raw-store reducer synchronously on the ingest dispatch; mirror that here so the
        // post-ingest store is visible when HandleAddEvent rebuilds the display view over it.
        mockDispatcher
            .When(d => d.Dispatch(Arg.Any<IngestRawEventsAction>()))
            .Do(callInfo => rawState = RawEventStoreReducers.ReduceIngestRawEvents(rawState, callInfo.Arg<IngestRawEventsAction>()));

        var newEvent = FilterEventBuilder.CreateTestEvent(100, logName: Constants.LogNameTestLog);
        var action = new AddEventAction(newEvent);

        // Act
        await effects.HandleAddEvent(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<IngestRawEventsAction>(a =>
            a.Mode == RawIngestMode.Prepend && a.EventsByLog.ContainsKey(logData.Id)));

        mockDispatcher.Received(1)
            .Dispatch(Arg.Is<AppendTableEventsAction>(a =>
                a.LogId == logData.Id && a.View != null && a.View.Count == 1 && a.View.EnumerateDetail().First().Id == newEvent.Id));

        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<DisplayReadyAction>());
    }

    [Fact]
    public async Task HandleAddEvent_WhenLogNotActive_ShouldNotDispatchActions()
    {
        // Arrange
        var (effects, mockDispatcher) = CreateEffects();
        var newEvent = FilterEventBuilder.CreateTestEvent(100, logName: Constants.LogNameTestLog);
        var action = new AddEventAction(newEvent);

        // Act
        await effects.HandleAddEvent(action, mockDispatcher);

        // Assert: No dispatches should occur
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<object>());
    }

    [Fact]
    public async Task HandleApplyFilter_FilterBranch_ShouldBracketDisplayedEventsUpdateWithFilterProgress()
    {
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher, _, _, _) = CreateEffectsWithServices(activeLogs: activeLogs);

        var filter = FilterBuilder.CreateTestFilter(isEnabled: true);
        var action = new ApplyFilterAction(new Filter(null, [filter]));

        await effects.HandleApplyFilter(action, mockDispatcher);

        Received.InOrder(() =>
        {
            mockDispatcher.Dispatch(Arg.Is<SetFilterProgressAction>(a => a.IsLoading));
            mockDispatcher.Dispatch(Arg.Any<DisplayReadyAction>());
            mockDispatcher.Dispatch(Arg.Is<SetFilterProgressAction>(a => !a.IsLoading));
        });
    }

    [Fact]
    public async Task HandleApplyFilter_FilterBranch_WhenFilterServiceThrows_ShouldStillClearFilterProgress()
    {
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var raw = RawEventList.Empty.Append(new List<ResolvedEvent> { FilterEventBuilder.CreateTestEvent(100) });

        var state = new EventLogState
        {
            ContinuouslyUpdate = false,
            OpenLogs = ImmutableDictionary<string, OpenLogInfo>.Empty
                .Add(Constants.LogNameTestLog, new OpenLogInfo(logData.Id, LogPathType.Channel)),
            NewEventBuffer = [],
            AppliedFilter = new Filter(null, [])
        };

        var seeded = new RawEventStoreState
        {
            ByLog = ImmutableDictionary<EventLogId, EventColumnStore>.Empty.Add(logData.Id, EventColumnStore.Build(raw, 0, 0))
        };

        var reads = 0;

        var (effects, mockDispatcher, _) = CreateEffectsWithMutableState(
            () => state,
            () =>
            {
                // The pass-1 snapshot (read 1) succeeds and the progress spinner turns on; the post-build
                // store re-check (read 2, inside the try) faults. The finally must still clear the spinner.
                if (Interlocked.Increment(ref reads) >= 2)
                {
                    throw new InvalidOperationException("boom");
                }

                return seeded;
            });

        var action = new ApplyFilterAction(new Filter(null, []));

        await Assert.ThrowsAsync<InvalidOperationException>(() => effects.HandleApplyFilter(action, mockDispatcher));

        mockDispatcher.Received(1).Dispatch(Arg.Is<SetFilterProgressAction>(a => a.IsLoading));
        mockDispatcher.Received(1).Dispatch(Arg.Is<SetFilterProgressAction>(a => !a.IsLoading));
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<DisplayReadyAction>());
    }

    [Fact]
    public async Task HandleApplyFilter_FilterBranch_WhenFinalizeArrivesDuringOffThreadBuild_ShouldRefilterAndReflectIt()
    {
        var snapshotData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var snapshotRaw = RawEventList.Empty.Append(new List<ResolvedEvent> { FilterEventBuilder.CreateTestEvent(200) });

        var finalizedRaw = RawEventList.Empty.Append(new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(200),
            FilterEventBuilder.CreateTestEvent(201),
            FilterEventBuilder.CreateTestEvent(202)
        });

        var state = new EventLogState
        {
            ContinuouslyUpdate = false,
            OpenLogs = ImmutableDictionary<string, OpenLogInfo>.Empty
                .Add(Constants.LogNameTestLog, new OpenLogInfo(snapshotData.Id, LogPathType.Channel)),
            NewEventBuffer = [],
            AppliedFilter = new Filter(null, [])
        };

        RawEventStoreState RawStateWith(RawEventList events, long contentVersion = 0) => new()
        {
            ByLog = ImmutableDictionary<EventLogId, EventColumnStore>.Empty.Add(snapshotData.Id, EventColumnStore.Build(events, 0, contentVersion))
        };

        var (effects, mockDispatcher, _) =
            CreateEffectsWithMutableState(
                () => state,
                SequencedRaw(RawStateWith(snapshotRaw), RawStateWith(finalizedRaw, 1)));

        // An empty filter passes every row, so the assertion can require the full post-race count.
        await effects.HandleApplyFilter(new ApplyFilterAction(new Filter(null, [])), mockDispatcher);

        // The pass-1 snapshot saw only event 200 at ContentVersion 0; the post-build re-check saw the
        // finalize rebuild at ContentVersion 1 and refiltered, so the published view reflects all three.
        mockDispatcher.Received(1)
            .Dispatch(Arg.Is<DisplayReadyAction>(a =>
                a.Views.ContainsKey(snapshotData.Id) &&
                a.Views[snapshotData.Id].Count == finalizedRaw.Count &&
                a.Views[snapshotData.Id].EnumerateDetail().Any(e => e.Id == 202)));
    }

    [Fact]
    public async Task HandleApplyFilter_FilterBranch_WhenLiveTailArrivesDuringOffThreadBuild_ShouldRefilterAndIncludeIt()
    {
        var snapshotData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var snapshotRaw = RawEventList.Empty.Append(new List<ResolvedEvent> { FilterEventBuilder.CreateTestEvent(100) });

        var liveTailRaw = RawEventList.Empty.Append(new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(100),
            FilterEventBuilder.CreateTestEvent(101)
        });

        var state = new EventLogState
        {
            ContinuouslyUpdate = false,
            OpenLogs = ImmutableDictionary<string, OpenLogInfo>.Empty
                .Add(Constants.LogNameTestLog, new OpenLogInfo(snapshotData.Id, LogPathType.Channel)),
            NewEventBuffer = [],
            AppliedFilter = new Filter(null, [])
        };

        RawEventStoreState RawStateWith(RawEventList events, long contentVersion = 0) => new()
        {
            ByLog = ImmutableDictionary<EventLogId, EventColumnStore>.Empty.Add(snapshotData.Id, EventColumnStore.Build(events, 0, contentVersion))
        };

        var (effects, mockDispatcher, _) =
            CreateEffectsWithMutableState(
                () => state,
                SequencedRaw(RawStateWith(snapshotRaw), RawStateWith(liveTailRaw, 1)));

        // An empty filter passes every row, so the assertion can require the full post-race count.
        await effects.HandleApplyFilter(new ApplyFilterAction(new Filter(null, [])), mockDispatcher);

        // The pass-1 snapshot saw only event 100 at ContentVersion 0; the post-build re-check saw the
        // live-tail append at ContentVersion 1 and refiltered, so the published view includes event 101.
        mockDispatcher.Received(1)
            .Dispatch(Arg.Is<DisplayReadyAction>(a =>
                a.Views.ContainsKey(snapshotData.Id) &&
                a.Views[snapshotData.Id].Count == liveTailRaw.Count &&
                a.Views[snapshotData.Id].EnumerateDetail().Any(e => e.Id == 101)));
    }

    [Fact]
    public async Task HandleApplyFilter_FilterBranch_WhenLogClosedDuringFilter_ShouldOmitStaleSliceFromDispatch()
    {
        // Arrange: the log closes (raw store and open-log map both drop it) while the filter
        // task is running. The post-filter dispatch must omit the closed log's slice. (The
        // reducer also skips unknown log ids, but checking at the effect keeps the dispatch minimal.)
        var snapshotEvents = new List<ResolvedEvent> { FilterEventBuilder.CreateTestEvent(100) };
        var snapshotData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var snapshotRaw = RawEventList.Empty.Append(snapshotEvents);

        var openState = new EventLogState
        {
            ContinuouslyUpdate = false,
            OpenLogs = ImmutableDictionary<string, OpenLogInfo>.Empty
                .Add(Constants.LogNameTestLog, new OpenLogInfo(snapshotData.Id, LogPathType.Channel)),
            NewEventBuffer = [],
            AppliedFilter = new Filter(null, [])
        };

        var openRaw = new RawEventStoreState
        {
            ByLog = ImmutableDictionary<EventLogId, EventColumnStore>.Empty.Add(snapshotData.Id, EventColumnStore.Build(snapshotRaw, 0, 0))
        };

        var closedRaw = new RawEventStoreState();

        var (effects, mockDispatcher, _) =
            CreateEffectsWithMutableState(() => openState, SequencedRaw(openRaw, closedRaw));

        var action = new ApplyFilterAction(new Filter(null, []));

        // Act
        await effects.HandleApplyFilter(action, mockDispatcher);

        // Assert: the raw store dropped the log during the off-thread build (the post-snapshot reads
        // return an empty store), so the post-build re-check finds it gone and DisplayReady omits its slice.
        mockDispatcher.Received(1)
            .Dispatch(Arg.Is<DisplayReadyAction>(a => !a.Views.ContainsKey(snapshotData.Id)));
    }

    [Fact]
    public async Task HandleApplyFilter_FilterBranch_WhenLogEventsChangeDuringFilter_ShouldRefilterFromCurrentState()
    {
        // Arrange: single open log; a live event arrives during the first filter pass (the raw
        // event list ref changes). The new filter must be re-applied to the post-mutation rows in
        // a single retry pass so the user sees the filter applied to the updated row set, not stale rows.
        var snapshotEvents = new List<ResolvedEvent> { FilterEventBuilder.CreateTestEvent(100) };
        var snapshotData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var snapshotRaw = RawEventList.Empty.Append(snapshotEvents);

        var liveTailEvents = new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(100),
            FilterEventBuilder.CreateTestEvent(101)
        };

        var liveTailRaw = RawEventList.Empty.Append(liveTailEvents);

        var state = new EventLogState
        {
            ContinuouslyUpdate = false,
            OpenLogs = ImmutableDictionary<string, OpenLogInfo>.Empty
                .Add(Constants.LogNameTestLog, new OpenLogInfo(snapshotData.Id, LogPathType.Channel)),
            NewEventBuffer = [],
            AppliedFilter = new Filter(null, [])
        };

        var snapshotRawState = new RawEventStoreState
        {
            ByLog = ImmutableDictionary<EventLogId, EventColumnStore>.Empty.Add(snapshotData.Id, EventColumnStore.Build(snapshotRaw, 0, 0))
        };

        var postLiveTailRawState = new RawEventStoreState
        {
            ByLog = ImmutableDictionary<EventLogId, EventColumnStore>.Empty.Add(snapshotData.Id, EventColumnStore.Build(liveTailRaw, 0, 1))
        };

        var (effects, mockDispatcher, _) =
            CreateEffectsWithMutableState(
                () => state,
                SequencedRaw(snapshotRawState, postLiveTailRawState));

        var action = new ApplyFilterAction(new Filter(null, []));

        // Act
        await effects.HandleApplyFilter(action, mockDispatcher);

        // Assert: the pass-1 snapshot saw ContentVersion 0 (event 100 only); the post-build re-check saw
        // the live-tail rebuild at ContentVersion 1 and refiltered from current state, so the published view
        // reflects the updated row set (event 101), not the stale pass-1 rows.
        mockDispatcher.Received(1)
            .Dispatch(Arg.Is<DisplayReadyAction>(a =>
                a.Views.ContainsKey(snapshotData.Id) &&
                a.Views[snapshotData.Id].Count == liveTailRaw.Count &&
                a.Views[snapshotData.Id].EnumerateDetail().Any(e => e.Id == 101)));
    }

    [Fact]
    public async Task HandleApplyFilter_FilterBranch_WhenLogStillStaleAfterRetry_ShouldOmitStaleSliceFromDispatch()
    {
        // Arrange: the raw event list changes during pass 1 AND again during pass 2. The pass-2 result is
        // still stale, so the slice is omitted (the reducer's carry-forward keeps the existing rows, avoiding
        // lost live events) AND a ConvergeFilterAction is scheduled to re-filter the log once it stabilizes.
        var snapshotEvents = new List<ResolvedEvent>();
        var snapshotData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var snapshotRaw = RawEventList.Empty.Append(snapshotEvents);

        var pass1MutationEvents = new List<ResolvedEvent> { FilterEventBuilder.CreateTestEvent(100) };

        var pass2MutationEvents = new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(100),
            FilterEventBuilder.CreateTestEvent(101)
        };

        var pass1MutationRaw = RawEventList.Empty.Append(pass1MutationEvents);
        var pass2MutationRaw = RawEventList.Empty.Append(pass2MutationEvents);

        var state = new EventLogState
        {
            ContinuouslyUpdate = false,
            OpenLogs = ImmutableDictionary<string, OpenLogInfo>.Empty
                .Add(Constants.LogNameTestLog, new OpenLogInfo(snapshotData.Id, LogPathType.Channel)),
            NewEventBuffer = [],
            AppliedFilter = new Filter(null, [])
        };

        RawEventStoreState RawStateWith(RawEventList events, long contentVersion = 0) => new()
        {
            ByLog = ImmutableDictionary<EventLogId, EventColumnStore>.Empty.Add(snapshotData.Id, EventColumnStore.Build(events, 0, contentVersion))
        };

        var (effects, mockDispatcher, _) =
            CreateEffectsWithMutableState(
                () => state,
                SequencedRaw(
                    RawStateWith(snapshotRaw),
                    RawStateWith(pass1MutationRaw, 1),
                    RawStateWith(pass2MutationRaw, 2),
                    RawStateWith(pass2MutationRaw, 3)));

        var action = new ApplyFilterAction(new Filter(null, []));

        // Act
        await effects.HandleApplyFilter(action, mockDispatcher);

        // Assert: pass 1 saw ContentVersion 0; the post-build re-check saw ContentVersion 1 (stale) and
        // refiltered; the pass-2 re-check saw ContentVersion 3 against the pass-2 snapshot's ContentVersion 2,
        // so the slice is still stale and omitted, and a convergence pass is scheduled while the progress
        // spinner stays on (cleared later by the converging pass).
        mockDispatcher.Received(1)
            .Dispatch(Arg.Is<DisplayReadyAction>(a => !a.Views.ContainsKey(snapshotData.Id)));

        mockDispatcher.Received(1)
            .Dispatch(Arg.Is<ConvergeFilterAction>(a => a.StaleIds.Contains(snapshotData.Id)));

        mockDispatcher.DidNotReceive().Dispatch(Arg.Is<SetFilterProgressAction>(a => !a.IsLoading));
    }

    [Fact]
    public async Task HandleApplyFilter_FilterBranch_WhenSupersededByNewerFilter_ShouldDropStaleResults()
    {
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);

        var state = new EventLogState
        {
            ContinuouslyUpdate = false,
            OpenLogs = ImmutableDictionary<string, OpenLogInfo>.Empty
                .Add(Constants.LogNameTestLog, new OpenLogInfo(logData.Id, LogPathType.Channel)),
            NewEventBuffer = [],
            AppliedFilter = new Filter(null, [])
        };

        var rawState = new RawEventStoreState
        {
            ByLog = ImmutableDictionary<EventLogId, EventColumnStore>.Empty.Add(
                logData.Id,
                EventColumnStore.Build(RawEventList.Empty.Append(new List<ResolvedEvent> { FilterEventBuilder.CreateTestEvent(100) }), 0, 0))
        };

        var (effects, mockDispatcher, _) =
            CreateEffectsWithMutableState(() => state, () => rawState);

        // The stale run's own progress-true dispatch stands in for a concurrent newer filter: it bumps the
        // filter token exactly once, so the stale run's post-build token guard drops it before it can publish.
        var superseded = 0;

        mockDispatcher
            .When(d => d.Dispatch(Arg.Is<SetFilterProgressAction>(a => a.IsLoading)))
            .Do(_ =>
            {
                if (Interlocked.Exchange(ref superseded, 1) == 0)
                {
                    effects.ConcurrencyState.InvalidateInFlightFilters();
                }
            });

        // The stale run is superseded mid-flight and must publish nothing.
        await effects.HandleApplyFilter(new ApplyFilterAction(new Filter(null, [])), mockDispatcher);

        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<DisplayReadyAction>());

        // A subsequent run captures the current token and lands normally.
        await effects.HandleApplyFilter(new ApplyFilterAction(new Filter(null, [])), mockDispatcher);

        mockDispatcher.Received(1)
            .Dispatch(Arg.Is<DisplayReadyAction>(a => a.Views[logData.Id].EnumerateDetail().Any(e => e.Id == 100)));

        // The stale run's finally was suppressed by the token guard, so only the fresh run cleared the spinner.
        mockDispatcher.Received(1).Dispatch(Arg.Is<SetFilterProgressAction>(a => !a.IsLoading));
    }

    [Fact]
    public async Task HandleApplyFilter_ReloadBranch_ShouldClearStaleFilterProgressSpinner()
    {
        // The reload path must dispatch SetFilterProgressAction(false) to clear any spinner
        // left over from a superseded filter-only run. Without this clear, the reload's
        // per-table IsLoading takes over the UI but the (stale) filter spinner would appear
        // stuck. The reload path itself must never dispatch SetFilterProgressAction(true);
        // per-table loading covers the close+reopen window.
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher, _, _, _) = CreateEffectsWithServices(activeLogs: activeLogs);

        // Route CloseLog to HandleCloseLog so the reload's close-completion TCS resolves
        // quickly. Without this routing, HandleApplyFilter parks on LogCloseTimeout (30s).
        var closeTasks = new List<Task>();

        mockDispatcher
            .When(d => d.Dispatch(Arg.Any<CloseLogAction>()))
            .Do(callInfo =>
            {
                closeTasks.Add(effects.HandleCloseLog(callInfo.Arg<CloseLogAction>(), mockDispatcher));
            });

        var xmlFilter = FilterBuilder.CreateTestFilter(FilterTestConstants.FilterXmlContainsData, isEnabled: true);
        var action = new ApplyFilterAction(new Filter(null, [xmlFilter]));

        await effects.HandleApplyFilter(action, mockDispatcher);

        Assert.True(action.Filter.RequiresXml);
        mockDispatcher.Received(1).Dispatch(Arg.Is<SetFilterProgressAction>(a => !a.IsLoading));
        mockDispatcher.DidNotReceive().Dispatch(Arg.Is<SetFilterProgressAction>(a => a.IsLoading));

        // Surface any HandleCloseLog faults before exiting the test.
        await Task.WhenAll(closeTasks);
    }

    [Fact]
    public async Task HandleApplyFilter_ShouldFilterAndDispatchUpdate()
    {
        // Arrange
        var events = new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(100, level: FilterTestConstants.EventLevelError),
            FilterEventBuilder.CreateTestEvent(200, level: FilterTestConstants.EventLevelInformation)
        };

        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher, _, _, _) = CreateEffectsWithServices(activeLogs: activeLogs);

        var filter = new Filter(null, []);
        var action = new ApplyFilterAction(filter);

        // Act
        await effects.HandleApplyFilter(action, mockDispatcher);

        // Assert: the columnar filter path publishes a DisplayReady for the active log.
        mockDispatcher.Received(1).Dispatch(Arg.Any<DisplayReadyAction>());
    }

    [Fact]
    public async Task HandleApplyFilter_WhenCloseAllArrivesMidReopenLoop_ShouldDispatchCloseLogForJustReopenedLogs()
    {
        // Arrange: pin down the per-iteration revalidation in the reopen loop. With two
        // logs needing reload, hook the FIRST OpenLogAction dispatch to land CloseAll
        // before the second iteration. The loop must:
        //   (1) detect the supersession on iteration 2,
        //   (2) dispatch CloseLogAction for the log it already reopened on iteration 1,
        //   (3) NOT dispatch the second OpenLog,
        //   (4) clear pending selection restore for both logs in the reload set.
        // Without per-iteration revalidation, both OpenLogs would land and re-add the logs
        // the user just closed.
        var logData1 = new EventLogData(Constants.LogNameLog1, LogPathType.Channel);
        var logData2 = new EventLogData(Constants.LogNameLog2, LogPathType.Channel);

        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty
            .Add(Constants.LogNameLog1, logData1)
            .Add(Constants.LogNameLog2, logData2);

        var (effects, mockDispatcher, mockLogWatcher, _, _) = CreateEffectsWithServices(activeLogs: activeLogs);

        var closeTasks = new List<Task>();

        mockDispatcher
            .When(d => d.Dispatch(Arg.Any<CloseLogAction>()))
            .Do(callInfo =>
            {
                closeTasks.Add(effects.HandleCloseLog(callInfo.Arg<CloseLogAction>(), mockDispatcher));
            });

        // Hook the FIRST OpenLogAction dispatch to land CloseAll synchronously, which bumps
        // reload token before the loop's next iteration check.
        var openLogCount = 0;

        mockDispatcher
            .When(d => d.Dispatch(Arg.Any<OpenLogAction>()))
            .Do(_ =>
            {
                openLogCount++;

                if (openLogCount == 1)
                {
                    // Run HandleCloseAll synchronously inside the dispatch hook so the next
                    // loop iteration sees the bumped reload token.
                    effects.HandleCloseAll(mockDispatcher).GetAwaiter().GetResult();
                }
            });

        var xmlFilter = FilterBuilder.CreateTestFilter(FilterTestConstants.FilterXmlContainsData, isEnabled: true);
        var filter = new Filter(null, [xmlFilter]);

        // Act
        await effects.HandleApplyFilter(new ApplyFilterAction(filter), mockDispatcher);

        // Assert: exactly one OpenLog dispatched (the second iteration bailed). A CloseLog
        // for the first-reopened log must have been dispatched as cleanup. The original two
        // CloseLog dispatches (reload-path close+await) plus the cleanup CloseLog = 3 total
        // CloseLog dispatches for these two log names.
        mockDispatcher.Received(1).Dispatch(Arg.Any<OpenLogAction>());

        // The cleanup CloseLog targets the log we already reopened (Log1).
        mockDispatcher.Received().Dispatch(Arg.Is<CloseLogAction>(a => a.LogName == Constants.LogNameLog1));

        // Surface any HandleCloseLog faults before exiting the test.
        await Task.WhenAll(closeTasks);
    }

    [Fact]
    public async Task HandleApplyFilter_WhenCloseAllSupersedesReload_ShouldClearPendingSelectionRestoreEntries()
    {
        // Arrange: pin down that when CloseAll supersedes a reload, the
        // _pendingSelectionRestore entries the reload wrote are cleared before bail-out.
        // Without this cleanup, a later manual reopen of the same log name would consume
        // stale selection state from the canceled reload (HandleLoadEvents reads
        // _pendingSelectionRestore unconditionally on each load).
        var selectedEvent = FilterEventBuilder.CreateTestEvent(100, recordId: 42, logName: Constants.LogNameTestLog);

        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var mockEventLogState = Substitute.For<IState<EventLogState>>();

        mockEventLogState.Value.Returns(new EventLogState
        {
            OpenLogs = ImmutableDictionary<string, OpenLogInfo>.Empty.Add(Constants.LogNameTestLog, new OpenLogInfo(logData.Id, LogPathType.Channel)),
            Selection = [RestoreEntry(selectedEvent, logData.Id)],
            AppliedFilter = new Filter(null, [])
        });

        var mockFilterService = Substitute.For<IFilterService>();
        var mockLogWatcher = Substitute.For<ILogWatcherService>();
        var watcherCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        mockLogWatcher.RemoveLogAsync(Arg.Any<string>()).Returns(watcherCompletion.Task);
        mockLogWatcher.RemoveAllAsync().Returns(Task.CompletedTask);

        var mockServiceScopeFactory = Substitute.For<IServiceScopeFactory>();
        var mockServiceScope = Substitute.For<IServiceScope>();
        mockServiceScopeFactory.CreateScope().Returns(mockServiceScope);
        mockServiceScope.ServiceProvider.Returns(Substitute.For<IServiceProvider>());

        var mockDatabaseService = Substitute.For<IDatabaseService>();
        mockDatabaseService.InitialClassificationTask.Returns(Task.CompletedTask);

        var mockDispatcher = Substitute.For<IDispatcher>();

        var effects = BuildHarness(
            mockEventLogState,
            EmptyRawStore(),
            mockFilterService,
            Substitute.For<ITraceLogger>(),
            mockLogWatcher,
            Substitute.For<IEventResolverCache>(),
            Substitute.For<IEventXmlResolver>(),
            mockServiceScopeFactory,
            mockDatabaseService,
            Substitute.For<ICriticalErrorService>(),
            mockDispatcher);

        var closeTasks = new List<Task>();

        mockDispatcher
            .When(d => d.Dispatch(Arg.Any<CloseLogAction>()))
            .Do(callInfo =>
            {
                closeTasks.Add(effects.HandleCloseLog(callInfo.Arg<CloseLogAction>(), mockDispatcher));
            });

        var xmlFilter = FilterBuilder.CreateTestFilter(FilterTestConstants.FilterXmlContainsData, isEnabled: true);
        var filter = new Filter(null, [xmlFilter]);

        // Act: start ApplyFilter; it writes the pending selection restore entry (selected
        // event has RecordId=42), then parks waiting for close-completion.
        var applyFilterTask = effects.HandleApplyFilter(new ApplyFilterAction(filter), mockDispatcher);

        // Land CloseAll while the reload is parked.
        await effects.HandleCloseAll(mockDispatcher);

        watcherCompletion.SetResult();
        await applyFilterTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Assert: a subsequent HandleLoadEvents for the same log name must find NO pending
        // selection restore entry (otherwise selection would be restored from the canceled
        // reload). HandleLoadEvents consumes the entry via TryRemove; if the entry isn't
        // there, no SetSelectedEvents dispatch occurs.
        var reopenLogData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);

        var reloadedEvents = ImmutableArray.Create(
            FilterEventBuilder.CreateTestEvent(100, recordId: 42, logName: Constants.LogNameTestLog));

        await effects.HandleLoadEvents(new LoadEventsAction(reopenLogData, reloadedEvents), mockDispatcher);

        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<SetSelectedEventsAction>());

        await Task.WhenAll(closeTasks);
    }

    [Fact]
    public async Task HandleApplyFilter_WhenCloseAllSupersedesReload_ShouldNotReopenClosedLogs()
    {
        // Arrange: pin down that a CloseAll landing WHILE ReloadLogsWithXmlAsync is parked
        // on the close-completion TCS suppresses the reopen loop. Without the version
        // recheck in ReloadLogsWithXmlAsync, the reload would re-add the just-closed logs
        // because OpenLogAction's reducer treats missing logs as adds.
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher, mockLogWatcher, _, _) = CreateEffectsWithServices(activeLogs: activeLogs);

        // Gate HandleCloseLog so the reload's close-completion TCS stays unsignaled until
        // we have a chance to land a CloseAll on the same dispatcher.
        var watcherCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        mockLogWatcher.RemoveLogAsync(Arg.Any<string>()).Returns(watcherCompletion.Task);

        var closeTasks = new List<Task>();

        mockDispatcher
            .When(d => d.Dispatch(Arg.Any<CloseLogAction>()))
            .Do(callInfo =>
            {
                closeTasks.Add(effects.HandleCloseLog(callInfo.Arg<CloseLogAction>(), mockDispatcher));
            });

        var xmlFilter = FilterBuilder.CreateTestFilter(FilterTestConstants.FilterXmlContainsData, isEnabled: true);
        var filter = new Filter(null, [xmlFilter]);

        // Act: start HandleApplyFilter; it parks waiting for the close-completion TCS.
        var applyFilterTask = effects.HandleApplyFilter(new ApplyFilterAction(filter), mockDispatcher);

        Assert.False(applyFilterTask.IsCompleted,
            "HandleApplyFilter must wait for HandleCloseLog before populating the restore map.");

        // While ReloadLogsWithXmlAsync is parked, simulate a CloseAll landing on the
        // dispatcher. This bumps reload token so the parked reload sees itself
        // as superseded and skips its reopen loop.
        await effects.HandleCloseAll(mockDispatcher);

        // Release HandleCloseLog; the close-completion TCS signals and the reload continues.
        watcherCompletion.SetResult();

        await applyFilterTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Assert: no OpenLogAction was dispatched for the closed log. The CloseLogAction
        // from the reload itself is fine; only the post-await reopen must be suppressed.
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<OpenLogAction>());

        // Surface any HandleCloseLog faults before exiting the test.
        await Task.WhenAll(closeTasks);
    }

    [Fact]
    public async Task HandleApplyFilter_WhenFilterDoesNotRequireXml_ShouldNotReloadLogs()
    {
        // Arrange: single active log + non-XML filter (Id-based). RequiresXml should be false,
        // so HandleApplyFilter should fall through to DisplayReady and never close/open logs.
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher, _, _, _) = CreateEffectsWithServices(activeLogs: activeLogs);

        var nonXmlFilter = FilterBuilder.CreateTestFilter(isEnabled: true);
        var filter = new Filter(null, [nonXmlFilter]);
        var action = new ApplyFilterAction(filter);

        // Act
        await effects.HandleApplyFilter(action, mockDispatcher);

        // Assert: DisplayReady path; no Close/Open dispatches.
        Assert.False(filter.RequiresXml);
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<CloseLogAction>());
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<OpenLogAction>());
        mockDispatcher.Received(1).Dispatch(Arg.Any<DisplayReadyAction>());
    }

    [Fact]
    public async Task HandleApplyFilter_WhenFilterRequiresXml_AwaitsCloseCompletionBeforeReturning()
    {
        // Arrange: RemoveLogAsync is gated by a controlled TCS so HandleCloseLog cannot
        // complete until the test signals it. HandleCloseLog clears _pendingSelectionRestore
        // for the log name AFTER awaiting the watcher, then signals the close-completion
        // TCS in its finally block. HandleApplyFilter must await that close-completion TCS
        // before populating _pendingSelectionRestore; otherwise the in-flight close wipes
        // the freshly-written entry. This test pins down the ordering invariant.
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher, mockLogWatcher, _, _) = CreateEffectsWithServices(activeLogs: activeLogs);

        var watcherCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        mockLogWatcher.RemoveLogAsync(Arg.Any<string>()).Returns(watcherCompletion.Task);

        // Capture each routed close so any fault in HandleCloseLog surfaces before the test
        // ends (the discarded `_ = effects.HandleCloseLog(...)` would otherwise let a fault
        // be silently observed by the finalizer).
        var closeTasks = new List<Task>();

        mockDispatcher
            .When(d => d.Dispatch(Arg.Any<CloseLogAction>()))
            .Do(callInfo =>
            {
                closeTasks.Add(effects.HandleCloseLog(callInfo.Arg<CloseLogAction>(), mockDispatcher));
            });

        var xmlFilter = FilterBuilder.CreateTestFilter(FilterTestConstants.FilterXmlContainsData, isEnabled: true);
        var filter = new Filter(null, [xmlFilter]);

        // Act: start HandleApplyFilter; it must remain pending until watcherCompletion fires.
        var applyFilterTask = effects.HandleApplyFilter(new ApplyFilterAction(filter), mockDispatcher);

        // Assert: task is blocked because HandleCloseLog can't finish until RemoveLogAsync
        // returns. Without the close-completion await in HandleApplyFilter, the task would
        // already be completed (it would have written the restore map and returned).
        Assert.False(applyFilterTask.IsCompleted,
            "HandleApplyFilter must wait for HandleCloseLog before populating the restore map.");

        // Release HandleCloseLog; the finally block signals the close-completion TCS.
        watcherCompletion.SetResult();

        await applyFilterTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // OpenLog should now have been dispatched (only happens after the close await).
        mockDispatcher.Received(1)
            .Dispatch(Arg.Is<OpenLogAction>(a =>
                a.LogName == Constants.LogNameTestLog && a.LogPathType == LogPathType.Channel));

        // Surface any HandleCloseLog faults before exiting the test.
        await Task.WhenAll(closeTasks);
    }

    [Fact]
    public async Task HandleApplyFilter_WhenFilterRequiresXml_ShouldRestoreSelectionAfterReload()
    {
        // Arrange: active log with one previously-selected event (RecordId=42). After the
        // XML filter triggers a reload, HandleLoadEvents should consume the pending restore
        // entry and dispatch SelectEvents with the matching event from the new event set.
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);

        var selectedEvent = FilterEventBuilder.CreateTestEvent(100, recordId: 42, logName: Constants.LogNameTestLog);

        // The reload's finalized rebuild is already in the store by the time HandleLoadEvents runs; seed it
        // so the effect builds the view and consumes the pending restore instead of early-returning.
        var reloadedEvents = new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(100, recordId: 42, logName: Constants.LogNameTestLog),
            FilterEventBuilder.CreateTestEvent(200, recordId: 99, logName: Constants.LogNameTestLog)
        };

        var mockRawStore = Substitute.For<IState<RawEventStoreState>>();

        mockRawStore.Value.Returns(new RawEventStoreState
        {
            ByLog = ImmutableDictionary<EventLogId, EventColumnStore>.Empty.Add(
                logData.Id, EventColumnStore.Build(RawEventList.Empty.Append(reloadedEvents), 0, 0))
        });

        var mockEventLogState = Substitute.For<IState<EventLogState>>();

        mockEventLogState.Value.Returns(new EventLogState
        {
            OpenLogs = ImmutableDictionary<string, OpenLogInfo>.Empty.Add(Constants.LogNameTestLog, new OpenLogInfo(logData.Id, LogPathType.Channel)),
            Selection = [RestoreEntry(selectedEvent, logData.Id)],
            AppliedFilter = new Filter(null, [])
        });

        var mockFilterService = Substitute.For<IFilterService>();

        mockFilterService.GetFilteredEvents(Arg.Any<IEnumerable<ResolvedEvent>>(), Arg.Any<Filter>())
            .Returns(callInfo => callInfo.Arg<IEnumerable<ResolvedEvent>>().ToList());

        var mockServiceScopeFactory = Substitute.For<IServiceScopeFactory>();
        var mockServiceScope = Substitute.For<IServiceScope>();
        var mockServiceProvider = Substitute.For<IServiceProvider>();

        mockServiceScopeFactory.CreateScope().Returns(mockServiceScope);
        mockServiceScope.ServiceProvider.Returns(mockServiceProvider);

        var effects = BuildHarness(
            mockEventLogState,
            mockRawStore,
            mockFilterService,
            Substitute.For<ITraceLogger>(),
            Substitute.For<ILogWatcherService>(),
            Substitute.For<IEventResolverCache>(),
            Substitute.For<IEventXmlResolver>(),
            mockServiceScopeFactory,
            Substitute.For<IDatabaseService>(),
            Substitute.For<ICriticalErrorService>(),
            Substitute.For<IDispatcher>());

        var mockDispatcher = Substitute.For<IDispatcher>();

        // Route CloseLog dispatches into HandleCloseLog so the close-completion TCSes
        // get signaled. HandleCloseLog clears _pendingSelectionRestore for the log name as
        // part of its async cleanup; HandleApplyFilter must await the close before writing
        // the restore map. Without this routing the await would hit LogCloseTimeout (30s).
        // Capture the routed tasks so any fault in HandleCloseLog surfaces at the end of
        // the test instead of being swallowed by the discard.
        var closeTasks = new List<Task>();

        mockDispatcher
            .When(d => d.Dispatch(Arg.Any<CloseLogAction>()))
            .Do(callInfo =>
            {
                closeTasks.Add(effects.HandleCloseLog(callInfo.Arg<CloseLogAction>(), mockDispatcher));
            });

        var xmlFilter = FilterBuilder.CreateTestFilter(FilterTestConstants.FilterXmlContainsData, isEnabled: true);
        var filter = new Filter(null, [xmlFilter]);

        // Act 1: Apply the XML filter, populating _pendingSelectionRestore for "TestLog".
        await effects.HandleApplyFilter(new ApplyFilterAction(filter), mockDispatcher);

        // Act 2: Simulate the subsequent LoadEvents that the new OpenLog produces (the reloaded events,
        // seeded into the store above, include the previously-selected RecordId=42 plus an unrelated one).
        await effects.HandleLoadEvents(new LoadEventsAction(logData, reloadedEvents), mockDispatcher);

        // Assert: SetSelectedEvents dispatched with exactly the restored event (RecordId=42).
        mockDispatcher.Received(1)
            .Dispatch(Arg.Is<SetSelectedEventsAction>(a =>
                a.Selection.Count() == 1 && a.Selection.First().ReloadKey!.Value.RecordId == 42));

        // Surface any HandleCloseLog faults before exiting the test.
        await Task.WhenAll(closeTasks);
    }

    [Fact]
    public async Task HandleApplyFilter_WhenFilterRequiresXmlAndLogLacksXml_ShouldCloseAndReopenLog()
    {
        // Arrange: active log has not been loaded with XML, so it must be re-read.
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher, _, _, _) = CreateEffectsWithServices(activeLogs: activeLogs);

        // Route CloseLog to HandleCloseLog so HandleApplyFilter's await on the close-completion
        // TCS resolves quickly (otherwise it hits LogCloseTimeout, 30s). Capture the routed
        // tasks so any fault in HandleCloseLog surfaces at the end of the test instead of
        // being swallowed by the discard.
        var closeTasks = new List<Task>();

        mockDispatcher
            .When(d => d.Dispatch(Arg.Any<CloseLogAction>()))
            .Do(callInfo =>
            {
                closeTasks.Add(effects.HandleCloseLog(callInfo.Arg<CloseLogAction>(), mockDispatcher));
            });

        var xmlFilter = FilterBuilder.CreateTestFilter(FilterTestConstants.FilterXmlContainsData, isEnabled: true);
        var filter = new Filter(null, [xmlFilter]);
        var action = new ApplyFilterAction(filter);

        // Act
        await effects.HandleApplyFilter(action, mockDispatcher);

        // Assert
        Assert.True(filter.RequiresXml);

        mockDispatcher.Received(1)
            .Dispatch(Arg.Is<CloseLogAction>(a =>
                a.LogName == Constants.LogNameTestLog && a.LogId == logData.Id));

        mockDispatcher.Received(1)
            .Dispatch(Arg.Is<OpenLogAction>(a =>
                a.LogName == Constants.LogNameTestLog && a.LogPathType == LogPathType.Channel));

        // Reload path returns early; no DisplayReady until LoadEvents fires.
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<DisplayReadyAction>());

        // Surface any HandleCloseLog faults before exiting the test.
        await Task.WhenAll(closeTasks);
    }

    [Fact]
    public async Task HandleApplyFilter_WhenNewerApplyFilterRacesReload_ShouldStillReopenClosedLogs()
    {
        // Arrange: pin down that a newer ApplyFilter racing in while ReloadLogsWithXmlAsync
        // is parked must NOT cause the parked reload to skip its reopen. The newer filter
        // only bumps filter token; only CloseAll bumps reload token. Without
        // that distinction, the round-2 guard treated any token bump as supersession
        // and left the user's logs closed (the newer ApplyFilter sees empty OpenLogs
        // because the CloseLog reducer already removed them, finds nothing to reload, and
        // returns).
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher, mockLogWatcher, _, _) = CreateEffectsWithServices(activeLogs: activeLogs);

        var watcherCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        mockLogWatcher.RemoveLogAsync(Arg.Any<string>()).Returns(watcherCompletion.Task);

        var closeTasks = new List<Task>();

        mockDispatcher
            .When(d => d.Dispatch(Arg.Any<CloseLogAction>()))
            .Do(callInfo =>
            {
                closeTasks.Add(effects.HandleCloseLog(callInfo.Arg<CloseLogAction>(), mockDispatcher));
            });

        var xmlFilter1 = FilterBuilder.CreateTestFilter(FilterTestConstants.FilterXmlContainsData, isEnabled: true);
        var filter1 = new Filter(null, [xmlFilter1]);

        // Act: start the first ApplyFilter; it parks waiting for the close-completion TCS.
        var applyFilterTask = effects.HandleApplyFilter(new ApplyFilterAction(filter1), mockDispatcher);

        Assert.False(applyFilterTask.IsCompleted,
            "HandleApplyFilter must wait for HandleCloseLog before populating the restore map.");

        // Race a NEWER ApplyFilter in. Use a non-XML filter so it goes through the fast
        // ApplyFilterAndPublishAsync path (no reload needed), but it still bumps
        // filter token at the top of HandleApplyFilter. It must NOT bump
        // reload token, so the parked first reload should still reopen its log.
        var nonXmlFilter = FilterBuilder.CreateTestFilter(isEnabled: true);
        var filter2 = new Filter(null, [nonXmlFilter]);
        await effects.HandleApplyFilter(new ApplyFilterAction(filter2), mockDispatcher);

        // Release the parked HandleCloseLog so the first reload can proceed.
        watcherCompletion.SetResult();

        await applyFilterTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Assert: OpenLog WAS dispatched for the closed log by the first reload (which
        // would not have happened if the round-2 guard had treated filter2's filter token
        // bump as supersession).
        mockDispatcher.Received(1)
            .Dispatch(Arg.Is<OpenLogAction>(a =>
                a.LogName == Constants.LogNameTestLog && a.LogPathType == LogPathType.Channel));

        // Surface any HandleCloseLog faults before exiting the test.
        await Task.WhenAll(closeTasks);
    }

    [Fact]
    public async Task HandleApplyFilter_WhenReloadSupersedesFilterOnly_ShouldDropFilterOnlyResults()
    {
        // A reload-path ApplyFilter invalidates any in-flight filter-only run by bumping the
        // filter token (FilteringEffects.HandleApplyFilter calls InvalidateInFlightFilters
        // before awaiting the reload). The older filter-only run then holds a stale token, so
        // its post-build token guard must drop its DisplayReady rather than let it land on top
        // of the newer applied filter.
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);

        var state = new EventLogState
        {
            ContinuouslyUpdate = false,
            OpenLogs = ImmutableDictionary<string, OpenLogInfo>.Empty
                .Add(Constants.LogNameTestLog, new OpenLogInfo(logData.Id, LogPathType.Channel)),
            NewEventBuffer = [],
            AppliedFilter = new Filter(null, [])
        };

        var rawState = new RawEventStoreState
        {
            ByLog = ImmutableDictionary<EventLogId, EventColumnStore>.Empty.Add(
                logData.Id,
                EventColumnStore.Build(RawEventList.Empty.Append(new List<ResolvedEvent> { FilterEventBuilder.CreateTestEvent(999) }), 0, 0))
        };

        var (effects, mockDispatcher, _) =
            CreateEffectsWithMutableState(() => state, () => rawState);

        // Model the concurrent reload's token bump: invalidate in-flight filters exactly once,
        // on the stale run's own progress-true dispatch. That fires after the stale run captured
        // its token and before its post-build guard, so the guard drops the stale result just as
        // a real reload-path ApplyFilter would.
        var superseded = 0;

        mockDispatcher
            .When(d => d.Dispatch(Arg.Is<SetFilterProgressAction>(a => a.IsLoading)))
            .Do(_ =>
            {
                if (Interlocked.Exchange(ref superseded, 1) == 0)
                {
                    effects.ConcurrencyState.InvalidateInFlightFilters();
                }
            });

        var staleFilterModel = FilterBuilder.CreateTestFilter(FilterTestConstants.FilterIdEquals999, isEnabled: true);
        var staleFilter = new Filter(null, [staleFilterModel]);

        await effects.HandleApplyFilter(new ApplyFilterAction(staleFilter), mockDispatcher);

        // The stale filter-only run must NOT have dispatched its DisplayReady for id 999.
        mockDispatcher.DidNotReceive()
            .Dispatch(Arg.Is<DisplayReadyAction>(a =>
                a.Views.ContainsKey(logData.Id) && a.Views[logData.Id].EnumerateDetail().Any(e => e.Id == 999)));
    }

    [Fact]
    public async Task HandleCloseAll_DispatchesStateClearsBeforeWatcherDrain()
    {
        var (effects, mockDispatcher, mockLogWatcher, mockResolverCache, _) = CreateEffectsWithServices();

        var watcherTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        mockLogWatcher.RemoveAllAsync().Returns(watcherTcs.Task);

        var closeTask = effects.HandleCloseAll(mockDispatcher);

        Assert.False(closeTask.IsCompleted, "HandleCloseAll must still be awaiting RemoveAllAsync.");
        mockResolverCache.Received(1).ClearAll();

        watcherTcs.SetResult();
        await closeTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.True(closeTask.IsCompletedSuccessfully);
        await mockLogWatcher.Received(1).RemoveAllAsync();
    }

    [Fact]
    public async Task HandleCloseAll_ShouldClearAllResolvedXml()
    {
        // Arrange
        var mockEventLogState = Substitute.For<IState<EventLogState>>();

        mockEventLogState.Value.Returns(new EventLogState
        {
            AppliedFilter = new Filter(null, [])
        });

        var mockXmlResolver = Substitute.For<IEventXmlResolver>();
        var mockServiceScopeFactory = Substitute.For<IServiceScopeFactory>();
        var mockServiceScope = Substitute.For<IServiceScope>();

        mockServiceScopeFactory.CreateScope().Returns(mockServiceScope);
        mockServiceScope.ServiceProvider.Returns(Substitute.For<IServiceProvider>());

        var mockDispatcher = Substitute.For<IDispatcher>();

        var effects = BuildHarness(
            mockEventLogState,
            EmptyRawStore(),
            Substitute.For<IFilterService>(),
            Substitute.For<ITraceLogger>(),
            Substitute.For<ILogWatcherService>(),
            Substitute.For<IEventResolverCache>(),
            mockXmlResolver,
            mockServiceScopeFactory,
            Substitute.For<IDatabaseService>(),
            Substitute.For<ICriticalErrorService>(),
            mockDispatcher);

        // Act
        await effects.HandleCloseAll(mockDispatcher);

        // Assert
        mockXmlResolver.Received(1).ClearAll();
    }

    [Fact]
    public async Task HandleCloseAll_ShouldRemoveAllLogsAndClearCache()
    {
        // Arrange
        var (effects, mockDispatcher, mockLogWatcher, mockResolverCache, _) = CreateEffectsWithServices();

        // Act
        await effects.HandleCloseAll(mockDispatcher);

        // Assert
        await mockLogWatcher.Received(1).RemoveAllAsync();
        mockResolverCache.Received(1).ClearAll();
    }

    [Fact]
    public async Task HandleCloseLog_AwaitsWatcherShutdown_BeforeSignalingCloseCompletion()
    {
        // Arrange: RemoveLogAsync returns a TCS we control; HandleCloseLog must NOT
        // signal its close-completion TCS until that TCS finishes. Verifies the B2
        // ordering fix: per-event resolver scopes (and their pooled SQLite handles) must
        // drain before the coordinator believes the log is closed.
        var (effects, mockDispatcher, mockLogWatcher, _, _) = CreateEffectsWithServices();

        var watcherTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        mockLogWatcher.RemoveLogAsync(Arg.Any<string>()).Returns(watcherTcs.Task);

        var logId = EventLogId.Create();
        var action = new CloseLogAction(logId, Constants.LogNameTestLog);

        // Act
        var closeTask = effects.HandleCloseLog(action, mockDispatcher);

        // Give HandleCloseLog a chance to advance to the await.
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Assert: close has not completed because the watcher hasn't released yet.
        Assert.False(closeTask.IsCompleted, "HandleCloseLog should be blocked on RemoveLogAsync.");

        // Now release the watcher; HandleCloseLog should complete promptly.
        watcherTcs.SetResult();

        await closeTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.True(closeTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task HandleCloseLog_ShouldClearResolvedXmlForLog()
    {
        // Arrange: verify the IEventXmlResolver entry for the closed log is evicted so a
        // subsequent reopen doesn't return stale text from the previous log instance.
        var logId = EventLogId.Create();
        var mockEventLogState = Substitute.For<IState<EventLogState>>();

        mockEventLogState.Value.Returns(new EventLogState
        {
            AppliedFilter = new Filter(null, [])
        });

        var mockXmlResolver = Substitute.For<IEventXmlResolver>();
        var mockServiceScopeFactory = Substitute.For<IServiceScopeFactory>();
        var mockServiceScope = Substitute.For<IServiceScope>();

        mockServiceScopeFactory.CreateScope().Returns(mockServiceScope);
        mockServiceScope.ServiceProvider.Returns(Substitute.For<IServiceProvider>());

        var effects = BuildHarness(
            mockEventLogState,
            EmptyRawStore(),
            Substitute.For<IFilterService>(),
            Substitute.For<ITraceLogger>(),
            Substitute.For<ILogWatcherService>(),
            Substitute.For<IEventResolverCache>(),
            mockXmlResolver,
            mockServiceScopeFactory,
            Substitute.For<IDatabaseService>(),
            Substitute.For<ICriticalErrorService>(),
            Substitute.For<IDispatcher>());

        var mockDispatcher = Substitute.For<IDispatcher>();
        var action = new CloseLogAction(logId, Constants.LogNameTestLog);

        // Act
        await effects.HandleCloseLog(action, mockDispatcher);

        // Assert
        mockXmlResolver.Received(1).ClearXmlCacheForLog(Constants.LogNameTestLog);
    }

    [Fact]
    public async Task HandleCloseLog_ShouldRemoveLogAndDispatchCloseAction()
    {
        // Arrange
        var logId = EventLogId.Create();
        var (effects, mockDispatcher, mockLogWatcher, _, _) = CreateEffectsWithServices();
        var action = new CloseLogAction(logId, Constants.LogNameTestLog);

        // Act
        await effects.HandleCloseLog(action, mockDispatcher);

        // Assert
        await mockLogWatcher.Received(1).RemoveLogAsync(Constants.LogNameTestLog);

        mockDispatcher.Received(1)
            .Dispatch(Arg.Is<Runtime.LogTable.CloseLogAction>(a =>
                a.LogId == logId));
    }

    [Fact]
    public async Task HandleCloseLog_WhenLastLog_ShouldClearResolverCache()
    {
        // Arrange: state has no active logs (reducer already removed the last one)
        var logId = EventLogId.Create();
        var (effects, mockDispatcher, mockLogWatcher, mockResolverCache, _) = CreateEffectsWithServices();
        var action = new CloseLogAction(logId, Constants.LogNameTestLog);

        // Act
        await effects.HandleCloseLog(action, mockDispatcher);

        // Assert
        mockResolverCache.Received(1).ClearAll();
    }

    [Fact]
    public async Task HandleCloseLog_WhenOtherLogsRemain_ShouldNotClearResolverCache()
    {
        // Arrange: state still has another active log
        var logData = new EventLogData(Constants.LogNameLog1, LogPathType.Channel);

        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty
            .Add(Constants.LogNameLog1, logData);

        var (effects, mockDispatcher, _, mockResolverCache, _) = CreateEffectsWithServices(activeLogs: activeLogs);
        var closingLogId = EventLogId.Create();
        var action = new CloseLogAction(closingLogId, Constants.LogNameTestLog);

        // Act
        await effects.HandleCloseLog(action, mockDispatcher);

        // Assert
        mockResolverCache.DidNotReceive().ClearAll();
    }

    [Fact]
    public async Task HandleConvergeFilter_WhenSnapshotStable_PublishesFreshAndDoesNotReschedule()
    {
        // Arrange: the convergence target's raw list is stable across the pass; the converge must publish the
        // re-filtered slice, clear the progress spinner, and NOT schedule another ConvergeFilterAction.
        var events = new List<ResolvedEvent> { FilterEventBuilder.CreateTestEvent(100) };
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var raw = RawEventList.Empty.Append(events);

        var state = new EventLogState
        {
            ContinuouslyUpdate = false,
            OpenLogs = ImmutableDictionary<string, OpenLogInfo>.Empty
                .Add(Constants.LogNameTestLog, new OpenLogInfo(logData.Id, LogPathType.Channel)),
            NewEventBuffer = [],
            AppliedFilter = new Filter(null, [])
        };

        var rawState = new RawEventStoreState
        {
            ByLog = ImmutableDictionary<EventLogId, EventColumnStore>.Empty.Add(logData.Id, EventColumnStore.Build(raw, 0, 0))
        };

        var (effects, mockDispatcher, mockFilterService) =
            CreateEffectsWithMutableState(() => state, () => rawState);

        mockFilterService.FilterActiveLogs(Arg.Any<IReadOnlyList<(EventLogId Id, IReadOnlyList<ResolvedEvent> Events)>>(), Arg.Any<Filter>())
            .Returns(new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>> { [logData.Id] = raw });

        // Act
        await effects.HandleConvergeFilter(
            new ConvergeFilterAction([logData.Id], effects.ConcurrencyState.GetCurrentFilterToken()),
            mockDispatcher);

        // Assert: converged slice published, progress cleared, no further convergence scheduled.
        mockDispatcher.Received(1)
            .Dispatch(Arg.Is<DisplayReadyAction>(a => a.Views.ContainsKey(logData.Id)));

        mockDispatcher.Received(1).Dispatch(Arg.Is<SetFilterProgressAction>(a => !a.IsLoading));

        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<ConvergeFilterAction>());
    }

    [Fact]
    public async Task HandleConvergeFilter_WhenSupersededByNewerToken_DoesNotPublish()
    {
        // Arrange: a newer operation bumps the filter token while the converge pass is off-thread
        // building its view; the post-await token guard must drop the stale convergence without
        // dispatching DisplayReady.
        var events = new List<ResolvedEvent> { FilterEventBuilder.CreateTestEvent(100) };
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var raw = RawEventList.Empty.Append(events);

        var state = new EventLogState
        {
            ContinuouslyUpdate = false,
            OpenLogs = ImmutableDictionary<string, OpenLogInfo>.Empty
                .Add(Constants.LogNameTestLog, new OpenLogInfo(logData.Id, LogPathType.Channel)),
            NewEventBuffer = [],
            AppliedFilter = new Filter(null, [])
        };

        var rawState = new RawEventStoreState
        {
            ByLog = ImmutableDictionary<EventLogId, EventColumnStore>.Empty.Add(logData.Id, EventColumnStore.Build(raw, 0, 0))
        };

        // Model a newer operation superseding the converge pass mid-flight: the first raw-store read
        // HandleConvergeFilter performs (ResidualOpenStale, after its initial token check and before
        // the post-build guard) bumps the filter token exactly once. The post-build guard must then
        // drop the stale convergence without dispatching DisplayReady.
        EffectsHarness? harness = null;
        var superseded = 0;

        var (effects, mockDispatcher, _) = CreateEffectsWithMutableState(
            () => state,
            () =>
            {
                if (harness is { } inFlight && Interlocked.Exchange(ref superseded, 1) == 0)
                {
                    inFlight.ConcurrencyState.InvalidateInFlightFilters();
                }

                return rawState;
            });

        harness = effects;

        // Act
        await effects.HandleConvergeFilter(
            new ConvergeFilterAction([logData.Id], effects.ConcurrencyState.GetCurrentFilterToken()),
            mockDispatcher);

        // Assert: superseded run published nothing.
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<DisplayReadyAction>());
    }

    [Fact]
    public async Task HandleConvergeFilter_WhenTargetLogClosed_PrunesWithoutFilteringOrPublishing()
    {
        // Arrange: the convergence target closed before the converge ran; it must be pruned (no filter pass, no
        // DisplayReady, no re-schedule) and the progress spinner cleared so it cannot spin on a gone log.
        var closedId = EventLogId.Create();

        var state = new EventLogState
        {
            ContinuouslyUpdate = false,
            OpenLogs = ImmutableDictionary<string, OpenLogInfo>.Empty,
            NewEventBuffer = [],
            AppliedFilter = new Filter(null, [])
        };

        var rawState = new RawEventStoreState();

        var (effects, mockDispatcher, mockFilterService) =
            CreateEffectsWithMutableState(() => state, () => rawState);

        // Act
        await effects.HandleConvergeFilter(
            new ConvergeFilterAction([closedId], effects.ConcurrencyState.GetCurrentFilterToken()),
            mockDispatcher);

        // Assert: pruned to empty: no filtering, no publish, no re-converge; progress cleared.
        mockFilterService.DidNotReceive()
            .FilterActiveLogs(Arg.Any<IReadOnlyList<(EventLogId Id, IReadOnlyList<ResolvedEvent> Events)>>(), Arg.Any<Filter>());

        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<DisplayReadyAction>());
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<ConvergeFilterAction>());
        mockDispatcher.Received(1).Dispatch(Arg.Is<SetFilterProgressAction>(a => !a.IsLoading));
    }

    [Fact]
    public async Task HandleLoadEvents_ShouldFilterAndDispatchUpdateTable()
    {
        // Arrange: the raw-store reducer for LoadEventsAction runs synchronously before this effect
        // in production, so the finalized rebuild is already in the store. HandleLoadEvents builds the
        // columnar display view over that store (the filter is applied inside the build) and hands the
        // pre-built view to the reducer via UpdateTableAction.
        var events = new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(100, level: FilterTestConstants.EventLevelError),
            FilterEventBuilder.CreateTestEvent(200, level: FilterTestConstants.EventLevelInformation)
        };

        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);

        var state = new EventLogState
        {
            ContinuouslyUpdate = false,
            OpenLogs = ImmutableDictionary<string, OpenLogInfo>.Empty
                .Add(Constants.LogNameTestLog, new OpenLogInfo(logData.Id, LogPathType.Channel)),
            NewEventBuffer = [],
            AppliedFilter = new Filter(null, [])
        };

        var rawState = new RawEventStoreState
        {
            ByLog = ImmutableDictionary<EventLogId, EventColumnStore>.Empty.Add(
                logData.Id,
                EventColumnStore.Build(RawEventList.Empty.Append(events), 0, 0))
        };

        var (effects, mockDispatcher, _) =
            CreateEffectsWithMutableState(() => state, () => rawState);

        var action = new LoadEventsAction(logData, events);

        // Act
        await effects.HandleLoadEvents(action, mockDispatcher);

        // Assert: a pre-built display view for the finalized log is handed to the reducer.
        mockDispatcher.Received(1).Dispatch(Arg.Is<UpdateTableAction>(a =>
            a.LogId == logData.Id && a.View != null && a.View.Count == 2));
    }

    [Fact]
    public async Task HandleLoadNewEvents_ShouldIngestRawPrependFromBuffer()
    {
        var bufferedEvents = new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(100, logName: Constants.LogNameTestLog)
        };
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);
        var (effects, mockDispatcher) = CreateEffects(activeLogs: activeLogs, newEventBuffer: bufferedEvents);

        await effects.HandleLoadNewEvents(mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Is<IngestRawEventsAction>(a =>
            a.Mode == RawIngestMode.Prepend && a.EventsByLog.ContainsKey(logData.Id)));
    }

    [Fact]
    public async Task HandleLoadNewEvents_ShouldProcessBufferAndDispatchActions()
    {
        // Arrange
        var bufferedEvents = new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(100, logName: Constants.LogNameTestLog),
            FilterEventBuilder.CreateTestEvent(200, logName: Constants.LogNameTestLog)
        };

        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);

        var state = new EventLogState
        {
            ContinuouslyUpdate = false,
            OpenLogs = ImmutableDictionary<string, OpenLogInfo>.Empty
                .Add(Constants.LogNameTestLog, new OpenLogInfo(logData.Id, LogPathType.Channel)),
            NewEventBuffer = bufferedEvents,
            AppliedFilter = new Filter(null, [])
        };

        var rawState = new RawEventStoreState
        {
            ByLog = ImmutableDictionary<EventLogId, EventColumnStore>.Empty.Add(logData.Id, EventColumnStore.Empty)
        };

        var (effects, mockDispatcher, _) = CreateEffectsWithMutableState(() => state, () => rawState);

        // Mirror Fluxor's synchronous raw-store reducer on the ingest dispatch so the buffered events are
        // visible in the store when the batch view is built.
        mockDispatcher
            .When(d => d.Dispatch(Arg.Any<IngestRawEventsAction>()))
            .Do(callInfo => rawState = RawEventStoreReducers.ReduceIngestRawEvents(rawState, callInfo.Arg<IngestRawEventsAction>()));

        // Act
        await effects.HandleLoadNewEvents(mockDispatcher);

        // Assert
        mockDispatcher.Received(1)
            .Dispatch(Arg.Is<AppendTableEventsBatchAction>(a =>
                a.ViewsByLog.Count == 1 &&
                a.ViewsByLog.ContainsKey(logData.Id) &&
                a.ViewsByLog[logData.Id].Count == 2));

        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<DisplayReadyAction>());

        mockDispatcher.Received(1)
            .Dispatch(Arg.Is<EventBufferedAction>(a =>
                a.UpdatedBuffer.Count == 0 && a.IsFull == false));
    }

    [Fact]
    public async Task HandleLoadNewEvents_WhenAllEventsFiltered_ShouldNotDispatchAppendBatch()
    {
        // Arrange
        var bufferedEvents = new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(100, logName: Constants.LogNameTestLog)
        };

        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher, _, _, mockFilterService) =
            CreateEffectsWithServices(activeLogs: activeLogs, newEventBuffer: bufferedEvents);

        mockFilterService.GetFilteredEvents(Arg.Any<IEnumerable<ResolvedEvent>>(), Arg.Any<Filter>())
            .Returns(new List<ResolvedEvent>());

        // Act
        await effects.HandleLoadNewEvents(mockDispatcher);

        // Assert
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<AppendTableEventsBatchAction>());

        mockDispatcher.Received(1)
            .Dispatch(Arg.Is<EventBufferedAction>(a =>
                a.UpdatedBuffer.Count == 0 && a.IsFull == false));
    }

    [Fact]
    public async Task HandleLoadNewEvents_WhenBufferSpansMultipleLogs_ShouldGroupIntoSingleBatch()
    {
        // Arrange
        // FilterEventBuilder.CreateTestEvent always sets OwningLog="TestLog"; override via `with` to span 2 logs.
        var bufferedEvents = new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(100) with { OwningLog = Constants.LogNameApplication },
            FilterEventBuilder.CreateTestEvent(200) with { OwningLog = Constants.LogNameTestLog },
            FilterEventBuilder.CreateTestEvent(300) with { OwningLog = Constants.LogNameApplication }
        };

        var applicationLog = new EventLogData(Constants.LogNameApplication, LogPathType.Channel);
        var testLog = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);

        var state = new EventLogState
        {
            ContinuouslyUpdate = false,
            OpenLogs = ImmutableDictionary<string, OpenLogInfo>.Empty
                .Add(Constants.LogNameApplication, new OpenLogInfo(applicationLog.Id, LogPathType.Channel))
                .Add(Constants.LogNameTestLog, new OpenLogInfo(testLog.Id, LogPathType.Channel)),
            NewEventBuffer = bufferedEvents,
            AppliedFilter = new Filter(null, [])
        };

        var rawState = new RawEventStoreState
        {
            ByLog = ImmutableDictionary<EventLogId, EventColumnStore>.Empty
                .Add(applicationLog.Id, EventColumnStore.Empty)
                .Add(testLog.Id, EventColumnStore.Empty)
        };

        var (effects, mockDispatcher, _) = CreateEffectsWithMutableState(() => state, () => rawState);

        // Mirror Fluxor's synchronous raw-store reducer so the ingest of both logs is visible when the
        // single batch view is built.
        mockDispatcher
            .When(d => d.Dispatch(Arg.Any<IngestRawEventsAction>()))
            .Do(callInfo => rawState = RawEventStoreReducers.ReduceIngestRawEvents(rawState, callInfo.Arg<IngestRawEventsAction>()));

        AppendTableEventsBatchAction? captured = null;

        mockDispatcher
            .When(dispatcher => dispatcher.Dispatch(Arg.Any<AppendTableEventsBatchAction>()))
            .Do(call => captured = (AppendTableEventsBatchAction)call.Args()[0]);

        // Act
        await effects.HandleLoadNewEvents(mockDispatcher);

        // Assert: a single batched append covering both logs.
        mockDispatcher.Received(1).Dispatch(Arg.Any<AppendTableEventsBatchAction>());
        Assert.NotNull(captured);
        Assert.Equal(2, captured.ViewsByLog.Count);
        Assert.Equal(2, captured.ViewsByLog[applicationLog.Id].Count);
        Assert.Equal(1, captured.ViewsByLog[testLog.Id].Count);
    }

    [Fact]
    public async Task HandleOpenLog_AwaitsInitialClassificationTask_BeforeResolverConstruction()
    {
        // Arrange: block classification with a pending TCS so we can verify the resolver
        // is NOT looked up until classification completes. Determinism comes from the await
        // semantics: the IServiceProvider mock cannot be invoked while the await is parked
        // on an incomplete task, so an immediate post-call assertion is sufficient.
        var logData = new EventLogData(Constants.LogNameApplication, LogPathType.Channel);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameApplication, logData);

        var classificationTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var (effects, mockDispatcher, mockServiceProvider, _, mockDatabaseService) =
            CreateEffectsForOpenLogGuards(activeLogs);

        mockDatabaseService.InitialClassificationTask.Returns(classificationTcs.Task);

        var action = new OpenLogAction(Constants.LogNameApplication, LogPathType.Channel);

        // Act 1: start the open; await yields back at InitialClassificationTask.
        var openTask = effects.HandleOpenLog(action, mockDispatcher);

        // Assert 1: resolver lookup MUST NOT have been touched yet.
        mockServiceProvider.DidNotReceive().GetService(typeof(IEventResolver));

        // Act 2: release classification; let HandleOpenLog finish.
        classificationTcs.SetResult(true);
        await openTask;

        // Assert 2: resolver lookup happens after classification.
        mockServiceProvider.Received(1).GetService(typeof(IEventResolver));
    }

    [Fact]
    public async Task HandleOpenLog_LogClosedDuringClassificationAwait_DoesNotDispatchAddTable()
    {
        // Arrange: simulate the user closing the log (HandleCloseLog dispatch already removed
        // it from OpenLogs and canceled its CTS) while HandleOpenLog is parked on the
        // classification await. After the await releases, HandleOpenLog must bail BEFORE
        // calling LoadLogAsync; otherwise LoadLogAsync's AddTable dispatch would resurrect
        // a table entry the user already dismissed, leaving an orphan in LogTableState.
        var logData = new EventLogData(Constants.LogNameApplication, LogPathType.Channel);


        var classificationTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Use a mutable IState so we can flip OpenLogs to "log closed" partway through.
        var mockEventLogState = Substitute.For<IState<EventLogState>>();

        var initialState = new EventLogState
        {
            OpenLogs = ImmutableDictionary<string, OpenLogInfo>.Empty.Add(Constants.LogNameApplication, new OpenLogInfo(logData.Id, LogPathType.Channel)),
            AppliedFilter = new Filter(null, [])
        };

        mockEventLogState.Value.Returns(initialState);

        var mockServiceScopeFactory = Substitute.For<IServiceScopeFactory>();
        var mockServiceScope = Substitute.For<IServiceScope>();
        var mockServiceProvider = Substitute.For<IServiceProvider>();

        mockServiceScopeFactory.CreateScope().Returns(mockServiceScope);
        mockServiceScope.ServiceProvider.Returns(mockServiceProvider);

        var mockEventResolver = Substitute.For<IEventResolver>();
        mockServiceProvider.GetService(typeof(IEventResolver)).Returns(mockEventResolver);

        var mockDatabaseService = Substitute.For<IDatabaseService>();
        mockDatabaseService.InitialClassificationTask.Returns(classificationTcs.Task);

        var effects = BuildHarness(
            mockEventLogState,
            EmptyRawStore(),
            Substitute.For<IFilterService>(),
            Substitute.For<ITraceLogger>(),
            Substitute.For<ILogWatcherService>(),
            Substitute.For<IEventResolverCache>(),
            Substitute.For<IEventXmlResolver>(),
            mockServiceScopeFactory,
            mockDatabaseService,
            Substitute.For<ICriticalErrorService>(),
            Substitute.For<IDispatcher>());

        var mockDispatcher = Substitute.For<IDispatcher>();
        var action = new OpenLogAction(Constants.LogNameApplication, LogPathType.Channel);

        // Act 1: start the open; await yields back at InitialClassificationTask.
        var openTask = effects.HandleOpenLog(action, mockDispatcher);

        // Act 2: simulate HandleCloseLog: remove the log from OpenLogs.
        mockEventLogState.Value.Returns(new EventLogState
        {
            OpenLogs = ImmutableDictionary<string, OpenLogInfo>.Empty,
            AppliedFilter = new Filter(null, [])
        });

        // Act 3: release classification; HandleOpenLog should detect the missing log and bail.
        classificationTcs.SetResult(true);
        await openTask;

        // Assert: neither AddTable (would orphan in LogTableState) nor any resolver work
        // happened. The bail-out path returns silently after the post-await identity check.
        mockServiceProvider.DidNotReceive().GetService(typeof(IEventResolver));

        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<AddTableAction>());
    }

    [Fact]
    public async Task HandleOpenLog_ResolverThrows_CallsReportCritical_DoesNotPropagate()
    {
        // Arrange: resolver factory throws (e.g., DI graph misconfiguration). HandleOpenLog
        // must surface this as a Reload-tier banner via ICriticalErrorService.ReportCritical and
        // return cleanly instead of letting the exception escape the effect.
        var logData = new EventLogData(Constants.LogNameApplication, LogPathType.Channel);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameApplication, logData);

        var (effects, mockDispatcher, mockServiceProvider, mockCriticalErrorService, _) =
            CreateEffectsForOpenLogGuards(activeLogs);

        var thrown = new InvalidOperationException("resolver factory failed");
        mockServiceProvider.When(p => p.GetService(typeof(IEventResolver))).Do(_ => throw thrown);

        var action = new OpenLogAction(Constants.LogNameApplication, LogPathType.Channel);

        // Act: must not throw.
        await effects.HandleOpenLog(action, mockDispatcher);

        // Assert: exact exception forwarded to banner; no resolver-status dispatch fired.
        mockCriticalErrorService.Received(1).ReportCritical(thrown);

        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<SetResolverStatusAction>());
    }

    [Fact]
    public async Task HandleOpenLog_ReverseEagerLoad_DispatchesExactlyOneEagerPartialBeforeFinal()
    {
        const int total = 250;
        var fakeFactory = new FakeEventLogReaderFactory(
            new FakeEventLogReader(BuildReverseBatches(total, batchSize: 30), newestBookmark: "NEWEST"));

        var (openLog, dispatcher, _) = CreateEagerLoadEffects(fakeFactory);

        await openLog.HandleOpenLog(new OpenLogAction(Constants.LogNameApplication, LogPathType.Channel), dispatcher);

        // The in-memory load finishes well under the 3-second partial timer, so the eager dispatch is the only
        // partial, asserting the eager path fires exactly once.
        var partials = AllPartialActions(dispatcher);
        Assert.Single(partials);
        Assert.NotEmpty(partials[0].Events);
    }

    [Fact]
    public async Task HandleOpenLog_ReverseEagerLoad_EmptyLog_SeedsWatcherWithNullBookmark()
    {
        var fakeFactory = new FakeEventLogReaderFactory(new FakeEventLogReader([], newestBookmark: null));

        var (openLog, dispatcher, watcher) = CreateEagerLoadEffects(fakeFactory);

        await openLog.HandleOpenLog(new OpenLogAction(Constants.LogNameApplication, LogPathType.Channel), dispatcher);

        watcher.Received(1).AddLog(Constants.LogNameApplication, null, Arg.Any<bool>());
        Assert.Empty(SingleFinalEvents(dispatcher));
    }

    [Fact]
    public async Task HandleOpenLog_ReverseEagerLoad_FinalListContainsEveryEventOnceSortedDescending()
    {
        // 250 > the eager first-paint threshold (200), so the eager dispatch fires.
        const int total = 250;
        var fakeFactory = new FakeEventLogReaderFactory(
            new FakeEventLogReader(BuildReverseBatches(total, batchSize: 30), newestBookmark: "NEWEST"));

        // Delay the newest batch so older batches resolve and AddRange first, scrambling completion order. The
        // sequence-ordered drain (not a physical re-sort) still reassembles the final list in read order, so it stays
        // complete, deduplicated, and newest-first.
        var (openLog, dispatcher, _) = CreateEagerLoadEffects(
            fakeFactory,
            resolveDelayMs: recordId => recordId > total - 30 ? 15 : 0);

        await openLog.HandleOpenLog(new OpenLogAction(Constants.LogNameApplication, LogPathType.Channel), dispatcher);

        var finalIds = SingleFinalEvents(dispatcher).Select(resolved => resolved.RecordId).ToList();
        Assert.Equal(total, finalIds.Count);
        Assert.Equal(total, finalIds.Distinct().Count());
        Assert.Equal(
            Enumerable.Range(1, total).Select(id => (long?)id).OrderByDescending(id => id).ToList(),
            finalIds);
    }

    [Fact]
    public async Task HandleOpenLog_ReverseEagerLoad_NonDescendingArrival_HoldsPhysicalIndexStableAcrossFinalization()
    {
        // Regression (highlight cache + selection stale after the partial-load finalization rebuild): the highlight
        // cache and selection are keyed on EventLocator = (LogId, Generation, physical Index). Partials stream in
        // arrival order and finalization rebuilds the raw store at the SAME Generation. A finalization RecordId-DESC
        // re-sort would reassign physical indices while keeping Generation, so a partial-era locator would keep
        // passing the Generation guard yet resolve to a DIFFERENT physical row (stale highlight colors, wrong selected
        // event) whenever arrival order differs from RecordId-DESC. This corpus reaches that case: a null RecordId at
        // its read position, a non-monotonic read (5000), and a duplicate RecordId (997).
        var arrivalRecordIds = new List<long?> { 1000, 999, 998, null, 5000, 997, 997, 996 };
        for (long next = 995; arrivalRecordIds.Count < 230; next--) { arrivalRecordIds.Add(next); }

        var fakeFactory = new FakeEventLogReaderFactory(
            new FakeEventLogReader(BuildBatchesFromRecordIds(arrivalRecordIds, batchSize: 30), newestBookmark: "NEWEST"));

        var (openLog, dispatcher, _) = CreateEagerLoadEffects(fakeFactory);

        await openLog.HandleOpenLog(new OpenLogAction(Constants.LogNameApplication, LogPathType.Channel), dispatcher);

        // Finalization dispatches in physical read (arrival) order; no vestigial RecordId-DESC re-sort.
        var finalEvents = SingleFinalEvents(dispatcher);
        var finalRecordIds = finalEvents.Select(resolved => resolved.RecordId).ToList();
        Assert.Equal(arrivalRecordIds, finalRecordIds);

        // The eager partial is a positional PREFIX of the finalized list, so every physical Index a partial-era
        // locator captured still addresses the same row after the rebuild.
        var partialEvents = AllPartialEvents(dispatcher).ToList();
        var partialRecordIds = partialEvents.Select(resolved => resolved.RecordId).ToList();
        Assert.NotEmpty(partialRecordIds);
        Assert.Equal(finalRecordIds.Take(partialRecordIds.Count).ToList(), partialRecordIds);

        // Resolve the exact defect through the real locator machinery: a locator minted against the mid-load partial
        // store must address the SAME event through the finalized store. Rebuild both column stores the way the
        // reducers do at a shared Generation 0 (Build preserves input order, so physical index i == the i-th arrived
        // event) and probe the null-RecordId and non-monotonic rows, the positions a RecordId-DESC re-sort would move.
        var logId = EventLogId.Create();
        var partialReader = EventColumnStore.Build(partialEvents, generation: 0, contentVersion: 0).CreateReader(logId);
        var finalReader = EventColumnStore.Build(finalEvents, generation: 0, contentVersion: 1).CreateReader(logId);

        foreach (int probe in new[] { arrivalRecordIds.IndexOf(null), arrivalRecordIds.IndexOf(5000L) })
        {
            var partialEraLocator = partialReader.LocatorAt(probe);

            Assert.Equal(
                partialReader.GetDetailLean(partialEraLocator).RecordId,
                finalReader.GetDetailLean(partialEraLocator).RecordId);
        }
    }

    [Fact]
    public async Task HandleOpenLog_ReverseEagerLoad_PartialDeltasAreDisjointAndSubsetOfFinal()
    {
        const int total = 250;
        var fakeFactory = new FakeEventLogReaderFactory(
            new FakeEventLogReader(BuildReverseBatches(total, batchSize: 30), newestBookmark: "NEWEST"));

        var (openLog, dispatcher, _) = CreateEagerLoadEffects(fakeFactory);

        await openLog.HandleOpenLog(new OpenLogAction(Constants.LogNameApplication, LogPathType.Channel), dispatcher);

        var partialIds = AllPartialEvents(dispatcher).Select(resolved => resolved.RecordId).ToList();
        Assert.NotEmpty(partialIds);
        Assert.Equal(partialIds.Count, partialIds.Distinct().Count());

        var finalIds = SingleFinalEvents(dispatcher).Select(resolved => resolved.RecordId).ToHashSet();
        Assert.All(partialIds, id => Assert.Contains(id, finalIds));
    }

    [Fact]
    public async Task HandleOpenLog_ReverseEagerLoad_PartialDeltasAreSortedNewestFirstDespiteCompletionOrder()
    {
        const int total = 250; // > eager threshold (200) so a partial dispatches mid-load
        var fakeFactory = new FakeEventLogReaderFactory(
            new FakeEventLogReader(BuildReverseBatches(total, batchSize: 30), newestBookmark: "NEWEST"));

        // Delay a second-newest batch so it resolves after older batches, scrambling completion order WITHIN the
        // eager partial. The sequence-ordered drain (not a sort) still assembles each dispatched delta in read order,
        // so the eager first paint renders newest-first.
        var (openLog, dispatcher, _) = CreateEagerLoadEffects(
            fakeFactory,
            resolveDelayMs: recordId => recordId is >= 191 and <= 220 ? 30 : 0);

        await openLog.HandleOpenLog(new OpenLogAction(Constants.LogNameApplication, LogPathType.Channel), dispatcher);

        var partials = dispatcher.ReceivedCalls()
            .Select(call => call.GetArguments()[0])
            .OfType<LoadEventsPartialAction>()
            .ToList();

        Assert.NotEmpty(partials);

        foreach (var partial in partials)
        {
            var ids = partial.Events.Select(resolved => resolved.RecordId).ToList();
            Assert.Equal(ids.OrderByDescending(id => id).ToList(), ids);
        }
    }

    [Fact]
    public async Task HandleOpenLog_ReverseEagerLoad_PartialDeltasStayNewestFirstAcrossDeltasDespiteLateNewestBatch()
    {
        const int Total = 250;
        var fakeFactory = new FakeEventLogReaderFactory(
            new FakeEventLogReader(BuildReverseBatches(Total, batchSize: 30), newestBookmark: "NEWEST"));

        var (openLog, dispatcher, _) = CreateEagerLoadEffects(
            fakeFactory,
            resolveDelayMs: recordId => recordId > Total - 30 ? 25 : 0);

        await openLog.HandleOpenLog(new OpenLogAction(Constants.LogNameApplication, LogPathType.Channel), dispatcher);

        var partialIds = AllPartialEvents(dispatcher).Select(resolved => resolved.RecordId).ToList();
        Assert.NotEmpty(partialIds);

        // Strictly newest-first across every delta (no cross-delta inversion).
        Assert.Equal(partialIds.OrderByDescending(id => id).ToList(), partialIds);

        var finalIds = SingleFinalEvents(dispatcher).Select(resolved => resolved.RecordId).ToList();
        Assert.Equal(finalIds.Take(partialIds.Count).ToList(), partialIds);
    }

    [Fact]
    public async Task HandleOpenLog_ReverseEagerLoad_SeedsWatcherFromNewestBookmark()
    {
        var fakeFactory = new FakeEventLogReaderFactory(
            new FakeEventLogReader(BuildReverseBatches(50, batchSize: 30), newestBookmark: "NEWEST_BOOKMARK"));

        var (openLog, dispatcher, watcher) = CreateEagerLoadEffects(fakeFactory);

        await openLog.HandleOpenLog(new OpenLogAction(Constants.LogNameApplication, LogPathType.Channel), dispatcher);

        // Under a reverse read the watcher must resume from the newest event; the reader exposes only NewestBookmark.
        watcher.Received(1).AddLog(Constants.LogNameApplication, "NEWEST_BOOKMARK", Arg.Any<bool>());

        // Lock the activation contract: the load path requested a reverse (newest-first) read.
        Assert.True(fakeFactory.ReverseDirectionRequested);
    }

    [Fact]
    public async Task HandleOpenLog_ReverseEagerLoad_WhenReaderInvalid_SurfacesLoadFailureNotEmptyLog()
    {
        var fakeFactory = new FakeEventLogReaderFactory(
            new FakeEventLogReader([], newestBookmark: null) { IsValid = false, OpenErrorCode = 5 });

        var (openLog, dispatcher, watcher) = CreateEagerLoadEffects(fakeFactory);

        await openLog.HandleOpenLog(new OpenLogAction(Constants.LogNameApplication, LogPathType.Channel), dispatcher);

        // A log that fails to open surfaces an error instead of a misleading empty final load, and never seeds the watcher.
        dispatcher.Received().Dispatch(Arg.Is<SetResolverStatusAction>(a =>
            a.ResolverStatus.Contains("Error") && a.ResolverStatus.Contains(Constants.LogNameApplication)));
        Assert.Empty(dispatcher.ReceivedCalls().Select(call => call.GetArguments()[0]).OfType<LoadEventsAction>());
        watcher.DidNotReceive().AddLog(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task HandleOpenLog_ReverseEagerLoad_WhenReadStopsOnError_SurfacesLoadFailureNotFinalLoad()
    {
        const int total = 60;
        var fakeFactory = new FakeEventLogReaderFactory(
            new FakeEventLogReader(BuildReverseBatches(total, batchSize: 30), newestBookmark: "NEWEST")
            {
                LastErrorCode = 5 // a non-null error once the batches run out: a failed read, not a clean end-of-results
            });

        var (openLog, dispatcher, _) = CreateEagerLoadEffects(fakeFactory);

        await openLog.HandleOpenLog(new OpenLogAction(Constants.LogNameApplication, LogPathType.Channel), dispatcher);

        // A read that stops on a Win32 error is surfaced as a failure, not dispatched as a successful final load.
        dispatcher.Received().Dispatch(Arg.Is<SetResolverStatusAction>(a =>
            a.ResolverStatus.Contains("Error") && a.ResolverStatus.Contains(Constants.LogNameApplication)));
        Assert.Empty(dispatcher.ReceivedCalls().Select(call => call.GetArguments()[0]).OfType<LoadEventsAction>());
    }

    [Fact]
    public async Task HandleOpenLog_ShouldThreadOpenLogsIdIntoDispatchedAddTableAction()
    {
        var logData = new EventLogData(Constants.LogNameApplication, LogPathType.Channel);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameApplication, logData);

        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var (effects, mockDispatcher) = CreateEffects(
            activeLogs: activeLogs,
            hasEventResolver: true);

        var action = new OpenLogAction(Constants.LogNameApplication, LogPathType.Channel, cts.Token);

        await effects.HandleOpenLog(action, mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Is<AddTableAction>(a => a.LogData.Id == logData.Id));
        mockDispatcher.Received().Dispatch(Arg.Is<CloseLogAction>(a => a.LogId == logData.Id));
    }

    [Fact]
    public async Task HandleOpenLog_WhenCancelled_ShouldDispatchCloseAndClearStatus()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameApplication, LogPathType.Channel);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameApplication, logData);

        var cts = new CancellationTokenSource();
        await cts.CancelAsync(); // Cancel immediately

        var (effects, mockDispatcher) = CreateEffects(
            activeLogs: activeLogs,
            hasEventResolver: true);

        var action = new OpenLogAction(Constants.LogNameApplication, LogPathType.Channel, cts.Token);

        // Act
        await effects.HandleOpenLog(action, mockDispatcher);

        // Assert
        mockDispatcher.Received().Dispatch(Arg.Any<CloseLogAction>());
        mockDispatcher.Received().Dispatch(Arg.Any<ClearStatusAction>());
    }

    [Fact]
    public async Task HandleOpenLog_WhenLogNotInOpenLogs_ShouldDispatchError()
    {
        // Arrange
        var (effects, mockDispatcher) = CreateEffects(hasEventResolver: true);
        var action = new OpenLogAction(Constants.LogNameTestLog, LogPathType.Channel);

        // Act
        await effects.HandleOpenLog(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1)
            .Dispatch(Arg.Is<SetResolverStatusAction>(a =>
                a.ResolverStatus.Contains("Error") && a.ResolverStatus.Contains(Constants.LogNameTestLog)));
    }

    [Fact]
    public async Task HandleOpenLog_WhenNoEventResolver_ShouldDispatchError()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameApplication, LogPathType.Channel);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameApplication, logData);

        var (effects, mockDispatcher) = CreateEffects(
            activeLogs: activeLogs,
            hasEventResolver: false);

        var action = new OpenLogAction(Constants.LogNameApplication, LogPathType.Channel);

        // Act
        await effects.HandleOpenLog(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1)
            .Dispatch(Arg.Is<SetResolverStatusAction>(a =>
                a.ResolverStatus.Contains("Error")));
    }

    [Fact]
    public async Task HandleSetContinuouslyUpdate_WhenFalse_ShouldNotProcessBuffer()
    {
        // Arrange
        var bufferedEvents = new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(100, logName: Constants.LogNameTestLog)
        };

        var (effects, mockDispatcher) = CreateEffects(newEventBuffer: bufferedEvents);
        var action = new SetContinuouslyUpdateAction(false);

        // Act
        await effects.HandleSetContinuouslyUpdate(action, mockDispatcher);

        // Assert
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<DisplayReadyAction>());
    }

    [Fact]
    public async Task HandleSetContinuouslyUpdate_WhenTrue_ShouldProcessBuffer()
    {
        // Arrange
        var bufferedEvents = new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(100, logName: Constants.LogNameTestLog)
        };

        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher) = CreateEffects(
            activeLogs: activeLogs,
            newEventBuffer: bufferedEvents);

        var action = new SetContinuouslyUpdateAction(true);

        // Act
        await effects.HandleSetContinuouslyUpdate(action, mockDispatcher);

        // Assert: ProcessNewEventBuffer now dispatches a batched append (no DisplayReady).
        mockDispatcher.Received(1).Dispatch(Arg.Any<AppendTableEventsBatchAction>());
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<DisplayReadyAction>());
    }

    [Fact]
    public async Task HandleSetOrderBy_SortEffect_ShouldRepublishUnderRequestedContextAtCapturedVersion()
    {
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);

        var events = new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(id: 1, source: "A"),
            FilterEventBuilder.CreateTestEvent(id: 2, source: "B")
        };
        var rawEvents = RawEventList.Empty.Append(events);

        var rawState = new RawEventStoreState
        {
            ByLog = ImmutableDictionary<EventLogId, EventColumnStore>.Empty.Add(logData.Id, EventColumnStore.Build(rawEvents, 0, 0))
        };

        var eventLogState = new EventLogState
        {
            ContinuouslyUpdate = false,
            OpenLogs = ImmutableDictionary<string, OpenLogInfo>.Empty
                .Add(Constants.LogNameTestLog, new OpenLogInfo(logData.Id, LogPathType.Channel)),
            NewEventBuffer = [],
            AppliedFilter = new Filter(null, [])
        };

        var logTableState = new LogTableState
        {
            RequestedOrderBy = ColumnName.Source,
            DisplayListVersion = 7
        };

        var (effects, mockDispatcher, _) =
            CreateEffectsWithMutableState(() => eventLogState, () => rawState, logTableState);

        await effects.Filtering.HandleSetOrderBy(new SetOrderByAction(ColumnName.Source), mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Is<DisplayReadyAction>(a =>
            a.Version == 7 &&
            a.Views.ContainsKey(logData.Id) &&
            a.Views[logData.Id].HasContext(new SortContext(ColumnName.Source, true, null, false))));
    }

    [Fact]
    public async Task HandleToggleGroupSorting_WhenNoPendingGroup_DoesNotRepublish()
    {
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);

        var rawState = new RawEventStoreState
        {
            ByLog = ImmutableDictionary<EventLogId, EventColumnStore>.Empty
        };

        var eventLogState = new EventLogState
        {
            ContinuouslyUpdate = false,
            OpenLogs = ImmutableDictionary<string, OpenLogInfo>.Empty
                .Add(Constants.LogNameTestLog, new OpenLogInfo(logData.Id, LogPathType.Channel)),
            NewEventBuffer = [],
            AppliedFilter = new Filter(null, [])
        };

        var logTableState = new LogTableState();

        var (effects, mockDispatcher, _) =
            CreateEffectsWithMutableState(() => eventLogState, () => rawState, logTableState);

        await effects.Filtering.HandleToggleGroupSorting(mockDispatcher);

        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<DisplayReadyAction>());
    }

    [Fact]
    public async Task HandleUpdateTable_WhenNoSortPending_DoesNotRepublish()
    {
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);

        var rawState = new RawEventStoreState
        {
            ByLog = ImmutableDictionary<EventLogId, EventColumnStore>.Empty.Add(
                logData.Id,
                EventColumnStore.Build(RawEventList.Empty.Append(new List<ResolvedEvent> { FilterEventBuilder.CreateTestEvent(id: 1, source: "A") }), 0, 0))
        };

        var eventLogState = new EventLogState
        {
            ContinuouslyUpdate = false,
            OpenLogs = ImmutableDictionary<string, OpenLogInfo>.Empty
                .Add(Constants.LogNameTestLog, new OpenLogInfo(logData.Id, LogPathType.Channel)),
            NewEventBuffer = [],
            AppliedFilter = new Filter(null, [])
        };

        var logTableState = new LogTableState
        {
            OrderBy = ColumnName.Source,
            RequestedOrderBy = ColumnName.Source
        };

        var (effects, mockDispatcher, _) =
            CreateEffectsWithMutableState(() => eventLogState, () => rawState, logTableState);

        await effects.Filtering.HandleUpdateTable(mockDispatcher);

        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<DisplayReadyAction>());
    }

    [Fact]
    public async Task HandleUpdateTable_WhenSortPending_RepublishesUnderRequestedContextAtCapturedVersion()
    {
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);

        var events = new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(id: 1, source: "A"),
            FilterEventBuilder.CreateTestEvent(id: 2, source: "B")
        };
        var rawEvents = RawEventList.Empty.Append(events);

        var rawState = new RawEventStoreState
        {
            ByLog = ImmutableDictionary<EventLogId, EventColumnStore>.Empty.Add(logData.Id, EventColumnStore.Build(rawEvents, 0, 0))
        };

        var eventLogState = new EventLogState
        {
            ContinuouslyUpdate = false,
            OpenLogs = ImmutableDictionary<string, OpenLogInfo>.Empty
                .Add(Constants.LogNameTestLog, new OpenLogInfo(logData.Id, LogPathType.Channel)),
            NewEventBuffer = [],
            AppliedFilter = new Filter(null, [])
        };

        var logTableState = new LogTableState
        {
            RequestedOrderBy = ColumnName.Source,
            DisplayListVersion = 7
        };

        var (effects, mockDispatcher, _) =
            CreateEffectsWithMutableState(() => eventLogState, () => rawState, logTableState);

        await effects.Filtering.HandleUpdateTable(mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Is<DisplayReadyAction>(a =>
            a.Version == 7 &&
            a.Views.ContainsKey(logData.Id) &&
            a.Views[logData.Id].HasContext(new SortContext(ColumnName.Source, true, null, false))));
    }

    [Fact]
    public async Task HandleUpdateTable_WhenSortPendingMultiLog_RepublishesEveryLogUnderRequested()
    {
        var logA = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var logB = new EventLogData("SecondTestLog", LogPathType.Channel);

        var eventsA = new List<ResolvedEvent> { FilterEventBuilder.CreateTestEvent(id: 1, source: "A") };
        var eventsB = new List<ResolvedEvent> { FilterEventBuilder.CreateTestEvent(id: 2, source: "B") };

        var rawState = new RawEventStoreState
        {
            ByLog = ImmutableDictionary<EventLogId, EventColumnStore>.Empty
                .Add(logA.Id, EventColumnStore.Build(RawEventList.Empty.Append(eventsA), 0, 0))
                .Add(logB.Id, EventColumnStore.Build(RawEventList.Empty.Append(eventsB), 0, 0))
        };

        var eventLogState = new EventLogState
        {
            ContinuouslyUpdate = false,
            OpenLogs = ImmutableDictionary<string, OpenLogInfo>.Empty
                .Add(Constants.LogNameTestLog, new OpenLogInfo(logA.Id, LogPathType.Channel))
                .Add("SecondTestLog", new OpenLogInfo(logB.Id, LogPathType.Channel)),
            NewEventBuffer = [],
            AppliedFilter = new Filter(null, [])
        };

        var logTableState = new LogTableState
        {
            RequestedOrderBy = ColumnName.Source,
            DisplayListVersion = 3
        };

        var (effects, mockDispatcher, _) =
            CreateEffectsWithMutableState(() => eventLogState, () => rawState, logTableState);

        await effects.Filtering.HandleUpdateTable(mockDispatcher);

        var requested = new SortContext(ColumnName.Source, true, null, false);
        mockDispatcher.Received(1).Dispatch(Arg.Is<DisplayReadyAction>(a =>
            a.Version == 3 &&
            a.Views.ContainsKey(logA.Id) && a.Views[logA.Id].HasContext(requested) &&
            a.Views.ContainsKey(logB.Id) && a.Views[logB.Id].HasContext(requested)));
    }

    [Fact]
    public void ReopenAfterDatabaseRemoval_DispatchesOpenLogPerSnapshotEntry()
    {
        // Arrange
        var (effects, mockDispatcher, _, _, _) = CreateEffectsWithServices();
        var coordinator = (ILogReloadCoordinator)effects.DatabaseCoordination;

        var snapshot = new[]
        {
            new LogReopenInfo(Constants.LogNameLog1, LogPathType.Channel),
            new LogReopenInfo(Constants.LogNameLog2, LogPathType.File)
        };

        // Act
        coordinator.ReopenAfterDatabaseRemoval(snapshot);

        // Assert
        mockDispatcher.Received(1)
            .Dispatch(Arg.Is<OpenLogAction>(a =>
                a.LogName == Constants.LogNameLog1 && a.LogPathType == LogPathType.Channel));

        mockDispatcher.Received(1)
            .Dispatch(Arg.Is<OpenLogAction>(a =>
                a.LogName == Constants.LogNameLog2 && a.LogPathType == LogPathType.File));
    }

    private static List<LoadEventsPartialAction> AllPartialActions(IDispatcher dispatcher) =>
        dispatcher.ReceivedCalls()
            .Select(call => call.GetArguments()[0])
            .OfType<LoadEventsPartialAction>()
            .ToList();

    private static IEnumerable<ResolvedEvent> AllPartialEvents(IDispatcher dispatcher) =>
        AllPartialActions(dispatcher).SelectMany(partial => partial.Events);

    private static IReadOnlyList<EventRecord[]> BuildBatchesFromRecordIds(IReadOnlyList<long?> recordIds, int batchSize)
    {
        var batches = new List<EventRecord[]>();

        // Preserve the given arrival order verbatim (no reverse, no sort) so a test can drive a read whose order
        // differs from RecordId-DESC: null RecordIds at their read position, a non-monotonic read, or a duplicate id.
        for (int start = 0; start < recordIds.Count; start += batchSize)
        {
            int count = Math.Min(batchSize, recordIds.Count - start);
            var batch = new EventRecord[count];

            for (int offset = 0; offset < count; offset++)
            {
                batch[offset] = new EventRecord { RecordId = recordIds[start + offset] };
            }

            batches.Add(batch);
        }

        return batches;
    }

    private static EffectsHarness BuildHarness(
        IState<EventLogState> eventLogState,
        IState<RawEventStoreState> rawEventStore,
        IFilterService filterService,
        ITraceLogger logger,
        ILogWatcherService logWatcherService,
        IEventResolverCache resolverCache,
        IEventXmlResolver xmlResolver,
        IServiceScopeFactory serviceScopeFactory,
        IDatabaseService databaseService,
        ICriticalErrorService criticalErrorService,
        IDispatcher dispatcher,
        LogTableState? logTableStateValue = null)
    {
        var closeCoordinator = new LogCloseCoordinator();
        var concurrencyState = new EventLogConcurrencyState();

        var logTableState = Substitute.For<IState<LogTableState>>();
        logTableState.Value.Returns(logTableStateValue ?? new LogTableState());

        var coordinator = new PartialLoadCoordinator(dispatcher, rawEventStore, eventLogState, logTableState, Timeout.InfiniteTimeSpan);

        var filtering = new FilteringEffects(
            eventLogState,
            rawEventStore,
            logTableState,
            filterService,
            logger,
            closeCoordinator,
            concurrencyState,
            TimeSpan.Zero);

        var openLog = new OpenLogEffects(
            eventLogState,
            logger,
            logWatcherService,
            resolverCache,
            xmlResolver,
            serviceScopeFactory,
            databaseService,
            criticalErrorService,
            closeCoordinator,
            concurrencyState,
            coordinator,
            new EventLogReaderFactory());

        var logReload = new LogReloadEffects(
            eventLogState,
            logTableState,
            rawEventStore,
            filterService,
            closeCoordinator,
            coordinator);

        var databaseCoordination = new DatabaseCoordinationEffects(
            eventLogState,
            logger,
            closeCoordinator,
            dispatcher,
            Substitute.For<IEventLogCommands>());

        return new EffectsHarness(
            filtering,
            openLog,
            logReload,
            databaseCoordination,
            closeCoordinator,
            concurrencyState);
    }

    private static (ImmutableDictionary<string, OpenLogInfo> OpenLogs, IState<RawEventStoreState> RawStore)
        BuildOpenLogsAndRawStore(ImmutableDictionary<string, EventLogData> activeLogs)
    {
        var openLogs = ImmutableDictionary<string, OpenLogInfo>.Empty;
        var byLog = ImmutableDictionary<EventLogId, EventColumnStore>.Empty;

        foreach (var (name, data) in activeLogs)
        {
            openLogs = openLogs.SetItem(name, new OpenLogInfo(data.Id, data.Type));
            byLog = byLog.SetItem(data.Id, EventColumnStore.Empty);
        }

        var rawStore = Substitute.For<IState<RawEventStoreState>>();
        rawStore.Value.Returns(new RawEventStoreState { ByLog = byLog });

        return (openLogs, rawStore);
    }

    private static IReadOnlyList<EventRecord[]> BuildReverseBatches(int total, int batchSize)
    {
        var batches = new List<EventRecord[]>();

        // Newest first (descending RecordId), mirroring a reverse read.
        for (int start = total; start >= 1; start -= batchSize)
        {
            int count = Math.Min(batchSize, start);
            var batch = new EventRecord[count];

            for (int offset = 0; offset < count; offset++)
            {
                batch[offset] = new EventRecord { RecordId = start - offset };
            }

            batches.Add(batch);
        }

        return batches;
    }

    private static (OpenLogEffects openLog, IDispatcher dispatcher, ILogWatcherService watcher) CreateEagerLoadEffects(
        IEventLogReaderFactory readerFactory,
        Func<long?, int>? resolveDelayMs = null)
    {
        var logData = new EventLogData(Constants.LogNameApplication, LogPathType.Channel);

        var openLogs = ImmutableDictionary<string, OpenLogInfo>.Empty
            .SetItem(Constants.LogNameApplication, new OpenLogInfo(logData.Id, logData.Type));

        var eventLogState = Substitute.For<IState<EventLogState>>();
        eventLogState.Value.Returns(new EventLogState
        {
            OpenLogs = openLogs,
            AppliedFilter = new Filter(null, [])
        });

        var resolver = Substitute.For<IEventResolver>();
        resolver.ResolveEvent(Arg.Any<EventRecord>()).Returns(callInfo =>
        {
            var record = callInfo.Arg<EventRecord>();

            if (resolveDelayMs is not null)
            {
                int delay = resolveDelayMs(record.RecordId);

                if (delay > 0) { Thread.Sleep(delay); }
            }

            return FilterEventBuilder.CreateTestEvent(recordId: record.RecordId);
        });

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IEventResolver)).Returns(resolver);

        var serviceScope = Substitute.For<IServiceScope>();
        serviceScope.ServiceProvider.Returns(serviceProvider);

        var serviceScopeFactory = Substitute.For<IServiceScopeFactory>();
        serviceScopeFactory.CreateScope().Returns(serviceScope);

        var databaseService = Substitute.For<IDatabaseService>();
        databaseService.InitialClassificationTask.Returns(Task.CompletedTask);

        var watcher = Substitute.For<ILogWatcherService>();
        watcher.RemoveLogAsync(Arg.Any<string>()).Returns(Task.CompletedTask);
        watcher.RemoveAllAsync().Returns(Task.CompletedTask);

        var dispatcher = Substitute.For<IDispatcher>();

        var rawEventStore = Substitute.For<IState<RawEventStoreState>>();
        rawEventStore.Value.Returns(new RawEventStoreState());

        var coordinatorLogTableState = Substitute.For<IState<LogTableState>>();
        coordinatorLogTableState.Value.Returns(new LogTableState());

        var openLog = new OpenLogEffects(
            eventLogState,
            Substitute.For<ITraceLogger>(),
            watcher,
            Substitute.For<IEventResolverCache>(),
            Substitute.For<IEventXmlResolver>(),
            serviceScopeFactory,
            databaseService,
            Substitute.For<ICriticalErrorService>(),
            new LogCloseCoordinator(),
            new EventLogConcurrencyState(),
            new PartialLoadCoordinator(dispatcher, rawEventStore, eventLogState, coordinatorLogTableState, Timeout.InfiniteTimeSpan),
            readerFactory);

        return (openLog, dispatcher, watcher);
    }

    private static (EffectsHarness effects, IDispatcher mockDispatcher) CreateEffects(
        bool continuouslyUpdate = false,
        ImmutableDictionary<string, EventLogData>? activeLogs = null,
        List<ResolvedEvent>? newEventBuffer = null,
        bool hasEventResolver = false)
    {
        var effectiveActiveLogs = activeLogs ?? ImmutableDictionary<string, EventLogData>.Empty;
        var (openLogs, rawStore) = BuildOpenLogsAndRawStore(effectiveActiveLogs);

        var mockEventLogState = Substitute.For<IState<EventLogState>>();

        mockEventLogState.Value.Returns(new EventLogState
        {
            ContinuouslyUpdate = continuouslyUpdate,
            OpenLogs = openLogs,
            NewEventBuffer = newEventBuffer ?? [],
            AppliedFilter = new Filter(null, [])
        });

        var mockFilterService = Substitute.For<IFilterService>();

        mockFilterService.FilterActiveLogs(Arg.Any<IReadOnlyList<(EventLogId Id, IReadOnlyList<ResolvedEvent> Events)>>(), Arg.Any<Filter>())
            .Returns(new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>>());

        mockFilterService.GetFilteredEvents(Arg.Any<IEnumerable<ResolvedEvent>>(), Arg.Any<Filter>())
            .Returns(callInfo => callInfo.Arg<IEnumerable<ResolvedEvent>>().ToList());

        var mockLogger = Substitute.For<ITraceLogger>();
        var mockLogWatcherService = Substitute.For<ILogWatcherService>();
        mockLogWatcherService.RemoveLogAsync(Arg.Any<string>()).Returns(Task.CompletedTask);
        mockLogWatcherService.RemoveAllAsync().Returns(Task.CompletedTask);
        var mockResolverCache = Substitute.For<IEventResolverCache>();

        var mockServiceScopeFactory = Substitute.For<IServiceScopeFactory>();
        var mockServiceScope = Substitute.For<IServiceScope>();
        var mockServiceProvider = Substitute.For<IServiceProvider>();

        mockServiceScopeFactory.CreateScope().Returns(mockServiceScope);
        mockServiceScope.ServiceProvider.Returns(mockServiceProvider);

        if (hasEventResolver)
        {
            var mockEventResolver = Substitute.For<IEventResolver>();

            mockEventResolver.ResolveEvent(Arg.Any<EventRecord>())
                .Returns(_ => FilterEventBuilder.CreateTestEvent(100));

            mockServiceProvider.GetService(typeof(IEventResolver)).Returns(mockEventResolver);
        }
        else
        {
            mockServiceProvider.GetService(typeof(IEventResolver)).Returns((IEventResolver?)null);
        }

        var mockDatabaseService = Substitute.For<IDatabaseService>();
        mockDatabaseService.InitialClassificationTask.Returns(Task.CompletedTask);

        var mockDispatcher = Substitute.For<IDispatcher>();

        var effects = BuildHarness(
            mockEventLogState,
            rawStore,
            mockFilterService,
            mockLogger,
            mockLogWatcherService,
            mockResolverCache,
            Substitute.For<IEventXmlResolver>(),
            mockServiceScopeFactory,
            mockDatabaseService,
            Substitute.For<ICriticalErrorService>(),
            mockDispatcher);

        return (effects, mockDispatcher);
    }

    private static (EffectsHarness effects,
        IDispatcher mockDispatcher,
        IServiceProvider mockServiceProvider,
        ICriticalErrorService mockCriticalErrorService,
        IDatabaseService mockDatabaseService) CreateEffectsForOpenLogGuards(
            ImmutableDictionary<string, EventLogData> activeLogs)
    {
        var (openLogs, rawStore) = BuildOpenLogsAndRawStore(activeLogs);

        var mockEventLogState = Substitute.For<IState<EventLogState>>();

        mockEventLogState.Value.Returns(new EventLogState
        {
            OpenLogs = openLogs,
            AppliedFilter = new Filter(null, [])
        });

        var mockServiceScopeFactory = Substitute.For<IServiceScopeFactory>();
        var mockServiceScope = Substitute.For<IServiceScope>();
        var mockServiceProvider = Substitute.For<IServiceProvider>();

        mockServiceScopeFactory.CreateScope().Returns(mockServiceScope);
        mockServiceScope.ServiceProvider.Returns(mockServiceProvider);

        var mockEventResolver = Substitute.For<IEventResolver>();

        mockEventResolver.ResolveEvent(Arg.Any<EventRecord>())
            .Returns(_ => FilterEventBuilder.CreateTestEvent(100));

        mockServiceProvider.GetService(typeof(IEventResolver)).Returns(mockEventResolver);

        var mockDatabaseService = Substitute.For<IDatabaseService>();
        mockDatabaseService.InitialClassificationTask.Returns(Task.CompletedTask);

        var mockCriticalErrorService = Substitute.For<ICriticalErrorService>();

        var mockDispatcher = Substitute.For<IDispatcher>();

        var effects = BuildHarness(
            mockEventLogState,
            rawStore,
            Substitute.For<IFilterService>(),
            Substitute.For<ITraceLogger>(),
            Substitute.For<ILogWatcherService>(),
            Substitute.For<IEventResolverCache>(),
            Substitute.For<IEventXmlResolver>(),
            mockServiceScopeFactory,
            mockDatabaseService,
            mockCriticalErrorService,
            mockDispatcher);

        return (effects, mockDispatcher, mockServiceProvider, mockCriticalErrorService, mockDatabaseService);
    }

    private static (EffectsHarness effects,
        IDispatcher mockDispatcher,
        IFilterService mockFilterService) CreateEffectsWithMutableState(
            Func<EventLogState> stateProvider,
            Func<RawEventStoreState> rawStateProvider,
            LogTableState? logTableStateValue = null)
    {
        var mockEventLogState = Substitute.For<IState<EventLogState>>();
        mockEventLogState.Value.Returns(_ => stateProvider());

        var mockRawEventStore = Substitute.For<IState<RawEventStoreState>>();
        mockRawEventStore.Value.Returns(_ => rawStateProvider());

        var mockFilterService = Substitute.For<IFilterService>();

        mockFilterService.FilterActiveLogs(Arg.Any<IReadOnlyList<(EventLogId Id, IReadOnlyList<ResolvedEvent> Events)>>(), Arg.Any<Filter>())
            .Returns(new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>>());

        mockFilterService.GetFilteredEvents(Arg.Any<IEnumerable<ResolvedEvent>>(), Arg.Any<Filter>())
            .Returns(callInfo => callInfo.Arg<IEnumerable<ResolvedEvent>>().ToList());

        var mockLogger = Substitute.For<ITraceLogger>();
        var mockLogWatcherService = Substitute.For<ILogWatcherService>();
        var mockResolverCache = Substitute.For<IEventResolverCache>();

        var mockServiceScopeFactory = Substitute.For<IServiceScopeFactory>();
        var mockServiceScope = Substitute.For<IServiceScope>();
        var mockServiceProvider = Substitute.For<IServiceProvider>();

        mockServiceScopeFactory.CreateScope().Returns(mockServiceScope);
        mockServiceScope.ServiceProvider.Returns(mockServiceProvider);

        var mockDatabaseService = Substitute.For<IDatabaseService>();
        mockDatabaseService.InitialClassificationTask.Returns(Task.CompletedTask);

        var mockDispatcher = Substitute.For<IDispatcher>();

        var effects = BuildHarness(
            mockEventLogState,
            mockRawEventStore,
            mockFilterService,
            mockLogger,
            mockLogWatcherService,
            mockResolverCache,
            Substitute.For<IEventXmlResolver>(),
            mockServiceScopeFactory,
            mockDatabaseService,
            Substitute.For<ICriticalErrorService>(),
            mockDispatcher,
            logTableStateValue);

        return (effects, mockDispatcher, mockFilterService);
    }

    private static (EffectsHarness effects,
        IDispatcher mockDispatcher,
        ILogWatcherService mockLogWatcher,
        IEventResolverCache mockResolverCache,
        IFilterService mockFilterService) CreateEffectsWithServices(
            bool continuouslyUpdate = false,
            ImmutableDictionary<string, EventLogData>? activeLogs = null,
            List<ResolvedEvent>? newEventBuffer = null)
    {
        var effectiveActiveLogs = activeLogs ?? ImmutableDictionary<string, EventLogData>.Empty;
        var (openLogs, rawStore) = BuildOpenLogsAndRawStore(effectiveActiveLogs);

        var mockEventLogState = Substitute.For<IState<EventLogState>>();

        mockEventLogState.Value.Returns(new EventLogState
        {
            ContinuouslyUpdate = continuouslyUpdate,
            OpenLogs = openLogs,
            NewEventBuffer = newEventBuffer ?? [],
            AppliedFilter = new Filter(null, [])
        });

        var mockFilterService = Substitute.For<IFilterService>();

        mockFilterService.FilterActiveLogs(Arg.Any<IReadOnlyList<(EventLogId Id, IReadOnlyList<ResolvedEvent> Events)>>(), Arg.Any<Filter>())
            .Returns(new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>>());

        mockFilterService.GetFilteredEvents(Arg.Any<IEnumerable<ResolvedEvent>>(), Arg.Any<Filter>())
            .Returns(callInfo => callInfo.Arg<IEnumerable<ResolvedEvent>>().ToList());

        var mockLogger = Substitute.For<ITraceLogger>();
        var mockLogWatcherService = Substitute.For<ILogWatcherService>();
        mockLogWatcherService.RemoveLogAsync(Arg.Any<string>()).Returns(Task.CompletedTask);
        mockLogWatcherService.RemoveAllAsync().Returns(Task.CompletedTask);
        var mockResolverCache = Substitute.For<IEventResolverCache>();

        var mockServiceScopeFactory = Substitute.For<IServiceScopeFactory>();
        var mockServiceScope = Substitute.For<IServiceScope>();
        var mockServiceProvider = Substitute.For<IServiceProvider>();

        mockServiceScopeFactory.CreateScope().Returns(mockServiceScope);
        mockServiceScope.ServiceProvider.Returns(mockServiceProvider);

        var mockDatabaseService = Substitute.For<IDatabaseService>();
        mockDatabaseService.InitialClassificationTask.Returns(Task.CompletedTask);

        var mockDispatcher = Substitute.For<IDispatcher>();

        var effects = BuildHarness(
            mockEventLogState,
            rawStore,
            mockFilterService,
            mockLogger,
            mockLogWatcherService,
            mockResolverCache,
            Substitute.For<IEventXmlResolver>(),
            mockServiceScopeFactory,
            mockDatabaseService,
            Substitute.For<ICriticalErrorService>(),
            mockDispatcher);

        return (effects, mockDispatcher, mockLogWatcherService, mockResolverCache, mockFilterService);
    }

    private static IState<RawEventStoreState> EmptyRawStore()
    {
        var rawStore = Substitute.For<IState<RawEventStoreState>>();
        rawStore.Value.Returns(new RawEventStoreState());
        return rawStore;
    }

    private static SelectionEntry RestoreEntry(ResolvedEvent evt, EventLogId logId, int index = 0)
    {
        var handle = new EventLocator(logId, 0, index);
        ValueKey.TryCreate(evt, out var reloadKey);

        return new SelectionEntry(handle, handle, reloadKey);
    }

    // Builds a raw-store provider whose reads model the columnar filter race guard (M1 monotonic
    // ContentVersion). The pass-1 snapshot is read #1; a store carrying a higher ContentVersion on
    // read #2 onward is how the effect detects a rebuild that landed during the off-thread build,
    // which drives the pass-2 refilter. Reads past the supplied set clamp to the last state.
    private static Func<RawEventStoreState> SequencedRaw(params RawEventStoreState[] statesByRead)
    {
        var reads = 0;

        return () =>
        {
            var index = Interlocked.Increment(ref reads) - 1;
            return statesByRead[Math.Min(index, statesByRead.Length - 1)];
        };
    }

    private static IReadOnlyList<ResolvedEvent> SingleFinalEvents(IDispatcher dispatcher) =>
        dispatcher.ReceivedCalls()
            .Select(call => call.GetArguments()[0])
            .OfType<LoadEventsAction>()
            .Single()
            .Events;

    // Wrapper that bundles the post-split effects classes together with their
    // shared singletons so existing tests keep their `effects.HandleXxx(...)`
    // call shape. Each method delegates to the appropriate split class.
    private sealed class EffectsHarness(
        FilteringEffects filtering,
        OpenLogEffects openLog,
        LogReloadEffects logReload,
        DatabaseCoordinationEffects databaseCoordination,
        LogCloseCoordinator closeCoordinator,
        EventLogConcurrencyState concurrencyState)
    {
        public LogCloseCoordinator CloseCoordinator { get; } = closeCoordinator;

        public EventLogConcurrencyState ConcurrencyState { get; } = concurrencyState;

        public DatabaseCoordinationEffects DatabaseCoordination { get; } = databaseCoordination;

        public FilteringEffects Filtering { get; } = filtering;

        public LogReloadEffects LogReload { get; } = logReload;

        public OpenLogEffects OpenLog { get; } = openLog;

        public Task HandleAddEvent(AddEventAction action, IDispatcher dispatcher) =>
            Filtering.HandleAddEvent(action, dispatcher);

        public Task HandleApplyFilter(ApplyFilterAction action, IDispatcher dispatcher) =>
            Filtering.HandleApplyFilter(action, dispatcher);

        public Task HandleCloseAll(IDispatcher dispatcher) => OpenLog.HandleCloseAll(dispatcher);

        public Task HandleCloseLog(CloseLogAction action, IDispatcher dispatcher) =>
            OpenLog.HandleCloseLog(action, dispatcher);

        public Task HandleConvergeFilter(ConvergeFilterAction action, IDispatcher dispatcher) =>
            Filtering.HandleConvergeFilter(action, dispatcher);

        public Task HandleLoadEvents(LoadEventsAction action, IDispatcher dispatcher) =>
            LogReload.HandleLoadEvents(action, dispatcher);

        public Task HandleLoadNewEvents(IDispatcher dispatcher) => LogReload.HandleLoadNewEvents(dispatcher);

        public Task HandleOpenLog(OpenLogAction action, IDispatcher dispatcher) =>
            OpenLog.HandleOpenLog(action, dispatcher);

        public Task HandleSetContinuouslyUpdate(SetContinuouslyUpdateAction action, IDispatcher dispatcher) =>
            Filtering.HandleSetContinuouslyUpdate(action, dispatcher);
    }

    private sealed class FakeEventLogReader(IReadOnlyList<EventRecord[]> batches, string? newestBookmark)
        : IEventLogReader
    {
        private int _index;

        public bool IsValid { get; init; } = true;

        public int? LastErrorCode { get; init; }

        public string? NewestBookmark { get; } = newestBookmark;

        public int? OpenErrorCode { get; init; }

        public void Dispose() { }

        public bool TryGetEvents(out EventRecord[] events, int batchSize = 30)
        {
            if (_index >= batches.Count)
            {
                events = [];

                return false;
            }

            events = batches[_index++];

            return true;
        }
    }

    private sealed class FakeEventLogReaderFactory(IEventLogReader reader) : IEventLogReaderFactory
    {
        public bool ReverseDirectionRequested { get; private set; }

        public IEventLogReader CreateReader(string path, LogPathType pathType, bool renderXml = false, bool reverseDirection = false)
        {
            ReverseDirectionRequested = reverseDirection;

            return reader;
        }
    }
}
