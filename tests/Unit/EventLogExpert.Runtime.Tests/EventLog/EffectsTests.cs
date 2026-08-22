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
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.StatusBar;
using EventLogExpert.Runtime.Tests.TestUtils;
using EventLogExpert.Runtime.Tests.TestUtils.Constants;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Collections.Immutable;
using CloseLogAction = EventLogExpert.Runtime.EventLog.CloseLogAction;
using Reducers = EventLogExpert.Runtime.EventLog.Reducers;

namespace EventLogExpert.Runtime.Tests.EventLog;

public sealed class EffectsTests
{
    private static readonly TimeSpan s_testTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Flush_WhenAnEventBuffersDuringIt_ConsumesOnlyTheSnapshotAndPreservesTheNewEvent()
    {
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var eventA = FilterEventBuilder.CreateTestEvent(100, logName: Constants.LogNameTestLog);
        var eventB = FilterEventBuilder.CreateTestEvent(200, logName: Constants.LogNameTestLog);

        var state = new EventLogState
        {
            ContinuouslyUpdate = false,
            OpenLogs = ImmutableDictionary<string, OpenLogInfo>.Empty
                .Add(Constants.LogNameTestLog, new OpenLogInfo(logData.Id, LogPathType.Channel)),
            NewEventBuffer = [eventA],
            AppliedFilter = new Filter(null, [])
        };

        var rawState = new RawEventStoreState
        {
            ByLog = ImmutableDictionary<EventLogId, EventColumnStore>.Empty.Add(logData.Id, EventColumnStore.Empty)
        };

        var (effects, mockDispatcher, _) = CreateEffectsWithMutableState(() => state, () => rawState);
        var pending = CaptureDispatchQueue(mockDispatcher);

        IReadOnlyList<ResolvedEvent> snapshot = [eventA];
        var rawByLog = new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>> { [logData.Id] = snapshot };

        pending.Enqueue(new IngestRawEventsAction(rawByLog, RawIngestMode.Prepend));
        pending.Enqueue(new NewEventBufferConsumedAction(snapshot));
        pending.Enqueue(new AddEventAction(eventB, logData.Id));

        while (pending.Count > 0)
        {
            switch (pending.Dequeue())
            {
                case IngestRawEventsAction ingest:
                    rawState = RawEventStoreReducers.ReduceIngestRawEvents(rawState, ingest);
                    break;
                case AddEventAction add:
                    state = Reducers.ReduceAddEvent(state, add);
                    await effects.HandleAddEvent(add, mockDispatcher);
                    break;
                case NewEventBufferConsumedAction consumed:
                    state = Reducers.ReduceNewEventBufferConsumed(state, consumed);
                    break;
            }
        }

        Assert.Single(state.NewEventBuffer);
        Assert.Same(eventB, state.NewEventBuffer[0]);
    }

    [Fact]
    public async Task HandleAddEvent_WhenContinuouslyUpdateFalse_DoesNotDispatch_BufferingIsReducerOnly()
    {
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher) = CreateEffects(false, activeLogs);

        var newEvent = FilterEventBuilder.CreateTestEvent(100, logName: Constants.LogNameTestLog);

        await effects.HandleAddEvent(new AddEventAction(newEvent, logData.Id), mockDispatcher);

        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<object>());
    }

    [Fact]
    public async Task HandleAddEvent_WhenContinuouslyUpdateTrue_AndEventFilteredOut_ShouldNotAppend()
    {
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

        var (effects, mockDispatcher, mockFilterService) = CreateEffectsWithMutableState(() => state, () => rawState);

        mockFilterService.GetFilteredEvents(Arg.Any<IEnumerable<ResolvedEvent>>(), Arg.Any<Filter>())
            .Returns(new List<ResolvedEvent>());

        var pending = CaptureDispatchQueue(mockDispatcher);
        var newEvent = FilterEventBuilder.CreateTestEvent(100, logName: Constants.LogNameTestLog);

        await effects.HandleAddEvent(new AddEventAction(newEvent, logData.Id), mockDispatcher);
        await DrainDispatchQueueAsync(pending, effects, mockDispatcher, () => rawState, r => rawState = r);

        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<NewEventBufferConsumedAction>());
    }

    [Fact]
    public async Task HandleAddEvent_WhenContinuouslyUpdateTrue_AndEventFilteredOut_ShouldStillIngestRaw()
    {
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher, _, _, mockFilterService) =
            CreateEffectsWithServices(true, activeLogs);

        mockFilterService.GetFilteredEvents(Arg.Any<IEnumerable<ResolvedEvent>>(), Arg.Any<Filter>())
            .Returns(new List<ResolvedEvent>());

        var newEvent = FilterEventBuilder.CreateTestEvent(100, logName: Constants.LogNameTestLog);

        await effects.HandleAddEvent(new AddEventAction(newEvent, logData.Id), mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Is<IngestRawEventsAction>(a => a != null &&
            a.Mode == RawIngestMode.Prepend && a.EventsByLog.ContainsKey(logData.Id)));
    }

    [Fact]
    public async Task HandleAddEvent_WhenContinuouslyUpdateTrue_ShouldIngestRaw()
    {
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

        var newEvent = FilterEventBuilder.CreateTestEvent(100, logName: Constants.LogNameTestLog);
        var action = new AddEventAction(newEvent, logData.Id);

        await effects.HandleAddEvent(action, mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Is<IngestRawEventsAction>(a => a != null &&
            a.Mode == RawIngestMode.Prepend && a.EventsByLog.ContainsKey(logData.Id)));
    }

    [Fact]
    public async Task HandleAddEvent_WhenLogNotActive_ShouldNotDispatchActions()
    {
        var (effects, mockDispatcher) = CreateEffects();
        var newEvent = FilterEventBuilder.CreateTestEvent(100, logName: Constants.LogNameTestLog);
        var action = new AddEventAction(newEvent, EventLogId.Create());

        await effects.HandleAddEvent(action, mockDispatcher);

        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<object>());
    }

    [Fact]
    public async Task HandleAddEvent_WhenSourceLogIdMismatchesOpenLog_ShouldNotDispatch()
    {
        // A stale watcher's event (old id) must not be routed into a same-name log reopened under a new id.
        var reopenedId = EventLogId.Create();

        var state = new EventLogState
        {
            ContinuouslyUpdate = true,
            OpenLogs = ImmutableDictionary<string, OpenLogInfo>.Empty
                .Add(Constants.LogNameTestLog, new OpenLogInfo(reopenedId, LogPathType.Channel)),
            NewEventBuffer = [],
            AppliedFilter = new Filter(null, [])
        };

        var rawState = new RawEventStoreState
        {
            ByLog = ImmutableDictionary<EventLogId, EventColumnStore>.Empty.Add(reopenedId, EventColumnStore.Empty)
        };

        var (effects, mockDispatcher, _) = CreateEffectsWithMutableState(() => state, () => rawState);

        var staleEvent = FilterEventBuilder.CreateTestEvent(100, logName: Constants.LogNameTestLog);

        await effects.HandleAddEvent(new AddEventAction(staleEvent, EventLogId.Create()), mockDispatcher);

        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<object>());
    }

    [Fact]
    public async Task HandleApplyFilter_WhenCloseAllArrivesMidReopenLoop_ShouldDispatchCloseLogForJustReopenedLogs()
    {
        var logData1 = new EventLogData(Constants.LogNameLog1, LogPathType.Channel);
        var logData2 = new EventLogData(Constants.LogNameLog2, LogPathType.Channel);

        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty
            .Add(Constants.LogNameLog1, logData1)
            .Add(Constants.LogNameLog2, logData2);

        var (effects, mockDispatcher, mockLogWatcher, _, _) = CreateEffectsWithServices(activeLogs: activeLogs, appliedFilter: XmlContainsFilter());

        var closeTasks = new List<Task>();

        mockDispatcher
            .When(d => d.Dispatch(Arg.Any<CloseLogAction>()))
            .Do(callInfo =>
            {
                closeTasks.Add(effects.HandleCloseLog(callInfo.ArgAt<CloseLogAction>(0), mockDispatcher));
            });

        var openLogCount = 0;

        mockDispatcher
            .When(d => d.Dispatch(Arg.Any<OpenLogAction>()))
            .Do(_ =>
            {
                openLogCount++;

                if (openLogCount == 1)
                {
                    effects.HandleCloseAll(mockDispatcher).GetAwaiter().GetResult();
                }
            });

        var xmlFilter = FilterBuilder.CreateTestFilter(FilterTestConstants.FilterXmlContainsData, isEnabled: true);
        var filter = new Filter(null, [xmlFilter]);

        await effects.HandleApplyFilter(new ApplyFilterAction(filter), mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Any<OpenLogAction>());

        mockDispatcher.Received().Dispatch(Arg.Is<CloseLogAction>(a => a != null && a.LogName == Constants.LogNameLog1));

        await Task.WhenAll(closeTasks);
    }

    [Fact]
    public async Task HandleApplyFilter_WhenCloseAllSupersedesReload_ShouldClearPendingSelectionRestoreEntries()
    {
        var selectedEvent = FilterEventBuilder.CreateTestEvent(100, recordId: 42, logName: Constants.LogNameTestLog);

        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var mockEventLogState = Substitute.For<IState<EventLogState>>();

        mockEventLogState.Value.Returns(new EventLogState
        {
            OpenLogs = ImmutableDictionary<string, OpenLogInfo>.Empty.Add(Constants.LogNameTestLog, new OpenLogInfo(logData.Id, LogPathType.Channel)),
            Selection = [RestoreEntry(selectedEvent, logData.Id)],
            AppliedFilter = XmlContainsFilter()
        });

        var mockFilterService = Substitute.For<IFilterService>();
        var mockLogWatcher = Substitute.For<ILogWatcherService>();
        var watcherCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        mockLogWatcher.RemoveLogAsync(Arg.Any<string>(), Arg.Any<EventLogId>()).Returns(watcherCompletion.Task);
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
                closeTasks.Add(effects.HandleCloseLog(callInfo.ArgAt<CloseLogAction>(0), mockDispatcher));
            });

        var xmlFilter = FilterBuilder.CreateTestFilter(FilterTestConstants.FilterXmlContainsData, isEnabled: true);
        var filter = new Filter(null, [xmlFilter]);

        var applyFilterTask = effects.HandleApplyFilter(new ApplyFilterAction(filter), mockDispatcher);

        await effects.HandleCloseAll(mockDispatcher);

        watcherCompletion.SetResult();
        await applyFilterTask.WaitAsync(s_testTimeout, TestContext.Current.CancellationToken);

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
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher, mockLogWatcher, _, _) = CreateEffectsWithServices(activeLogs: activeLogs, appliedFilter: XmlContainsFilter());

        var watcherCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        mockLogWatcher.RemoveLogAsync(Arg.Any<string>(), Arg.Any<EventLogId>()).Returns(watcherCompletion.Task);

        var closeTasks = new List<Task>();

        mockDispatcher
            .When(d => d.Dispatch(Arg.Any<CloseLogAction>()))
            .Do(callInfo =>
            {
                closeTasks.Add(effects.HandleCloseLog(callInfo.ArgAt<CloseLogAction>(0), mockDispatcher));
            });

        var xmlFilter = FilterBuilder.CreateTestFilter(FilterTestConstants.FilterXmlContainsData, isEnabled: true);
        var filter = new Filter(null, [xmlFilter]);

        var applyFilterTask = effects.HandleApplyFilter(new ApplyFilterAction(filter), mockDispatcher);

        Assert.False(applyFilterTask.IsCompleted,
            "HandleApplyFilter must wait for HandleCloseLog before populating the restore map.");

        await effects.HandleCloseAll(mockDispatcher);

        watcherCompletion.SetResult();

        await applyFilterTask.WaitAsync(s_testTimeout, TestContext.Current.CancellationToken);

        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<OpenLogAction>());

        await Task.WhenAll(closeTasks);
    }

    [Fact]
    public async Task HandleApplyFilter_WhenFilterDoesNotRequireXml_ShouldNotReloadLogs()
    {
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher, _, _, _) = CreateEffectsWithServices(activeLogs: activeLogs);

        var nonXmlFilter = FilterBuilder.CreateTestFilter(isEnabled: true);
        var filter = new Filter(null, [nonXmlFilter]);
        var action = new ApplyFilterAction(filter);

        await effects.HandleApplyFilter(action, mockDispatcher);

        Assert.False(filter.RequiresXml);
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<CloseLogAction>());
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<OpenLogAction>());
    }

    [Fact]
    public async Task HandleApplyFilter_WhenFilterRequiresXmlAndLogLacksXml_ShouldCloseAndReopenLog()
    {
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher, _, _, _) = CreateEffectsWithServices(activeLogs: activeLogs, appliedFilter: XmlContainsFilter());

        var closeTasks = new List<Task>();

        mockDispatcher
            .When(d => d.Dispatch(Arg.Any<CloseLogAction>()))
            .Do(callInfo =>
            {
                closeTasks.Add(effects.HandleCloseLog(callInfo.ArgAt<CloseLogAction>(0), mockDispatcher));
            });

        var xmlFilter = FilterBuilder.CreateTestFilter(FilterTestConstants.FilterXmlContainsData, isEnabled: true);
        var filter = new Filter(null, [xmlFilter]);
        var action = new ApplyFilterAction(filter);

        await effects.HandleApplyFilter(action, mockDispatcher);

        Assert.True(filter.RequiresXml);

        mockDispatcher.Received(1)
            .Dispatch(Arg.Is<CloseLogAction>(a => a != null &&
                a.LogName == Constants.LogNameTestLog && a.LogId == logData.Id));

        mockDispatcher.Received(1)
            .Dispatch(Arg.Is<OpenLogAction>(a => a != null &&
                a.LogName == Constants.LogNameTestLog && a.LogPathType == LogPathType.Channel));

        await Task.WhenAll(closeTasks);
    }

    [Fact]
    public async Task HandleApplyFilter_WhenFilterRequiresXml_AwaitsCloseCompletionBeforeReturning()
    {
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher, mockLogWatcher, _, _) = CreateEffectsWithServices(activeLogs: activeLogs, appliedFilter: XmlContainsFilter());

        var watcherCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        mockLogWatcher.RemoveLogAsync(Arg.Any<string>(), Arg.Any<EventLogId>()).Returns(watcherCompletion.Task);

        var closeTasks = new List<Task>();

        mockDispatcher
            .When(d => d.Dispatch(Arg.Any<CloseLogAction>()))
            .Do(callInfo =>
            {
                closeTasks.Add(effects.HandleCloseLog(callInfo.ArgAt<CloseLogAction>(0), mockDispatcher));
            });

        var xmlFilter = FilterBuilder.CreateTestFilter(FilterTestConstants.FilterXmlContainsData, isEnabled: true);
        var filter = new Filter(null, [xmlFilter]);

        var applyFilterTask = effects.HandleApplyFilter(new ApplyFilterAction(filter), mockDispatcher);

        Assert.False(applyFilterTask.IsCompleted,
            "HandleApplyFilter must wait for HandleCloseLog before populating the restore map.");

        watcherCompletion.SetResult();

        await applyFilterTask.WaitAsync(s_testTimeout, TestContext.Current.CancellationToken);

        mockDispatcher.Received(1)
            .Dispatch(Arg.Is<OpenLogAction>(a => a != null &&
                a.LogName == Constants.LogNameTestLog && a.LogPathType == LogPathType.Channel));

        await Task.WhenAll(closeTasks);
    }

    [Fact]
    public async Task HandleApplyFilter_WhenFilterRequiresXml_ShouldRestoreSelectionAfterReload()
    {
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);

        var selectedEvent = FilterEventBuilder.CreateTestEvent(100, recordId: 42, logName: Constants.LogNameTestLog);

        var reloadedEvents = new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(100, recordId: 42, logName: Constants.LogNameTestLog),
            FilterEventBuilder.CreateTestEvent(200, recordId: 99, logName: Constants.LogNameTestLog)
        };

        var mockRawStore = Substitute.For<IState<RawEventStoreState>>();

        mockRawStore.Value.Returns(new RawEventStoreState
        {
            ByLog = ImmutableDictionary<EventLogId, EventColumnStore>.Empty.Add(
                logData.Id, EventColumnStore.Build(reloadedEvents, 0, 0))
        });

        var mockEventLogState = Substitute.For<IState<EventLogState>>();

        mockEventLogState.Value.Returns(new EventLogState
        {
            OpenLogs = ImmutableDictionary<string, OpenLogInfo>.Empty.Add(Constants.LogNameTestLog, new OpenLogInfo(logData.Id, LogPathType.Channel)),
            Selection = [RestoreEntry(selectedEvent, logData.Id)],
            AppliedFilter = XmlContainsFilter()
        });

        var mockFilterService = Substitute.For<IFilterService>();

        mockFilterService.GetFilteredEvents(Arg.Any<IEnumerable<ResolvedEvent>>(), Arg.Any<Filter>())
            .Returns(callInfo => callInfo.ArgAt<IEnumerable<ResolvedEvent>>(0).ToList());

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

        var closeTasks = new List<Task>();

        mockDispatcher
            .When(d => d.Dispatch(Arg.Any<CloseLogAction>()))
            .Do(callInfo =>
            {
                closeTasks.Add(effects.HandleCloseLog(callInfo.ArgAt<CloseLogAction>(0), mockDispatcher));
            });

        var xmlFilter = FilterBuilder.CreateTestFilter(FilterTestConstants.FilterXmlContainsData, isEnabled: true);
        var filter = new Filter(null, [xmlFilter]);

        await effects.HandleApplyFilter(new ApplyFilterAction(filter), mockDispatcher);

        await effects.HandleLoadEvents(new LoadEventsAction(logData, reloadedEvents), mockDispatcher);

        mockDispatcher.Received(1)
            .Dispatch(Arg.Is<SetSelectedEventsAction>(a => a != null &&
                a.Selection.Count() == 1 && a.Selection.First().ReloadKey!.Value.RecordId == 42));

        mockDispatcher.Received(1)
            .Dispatch(Arg.Is<RequestRevealFocusAction>(a => a != null && a.Target == new EventLocator(logData.Id, 0, 0)));

        await Task.WhenAll(closeTasks);
    }

    [Fact]
    public async Task HandleApplyFilter_WhenNewerApplyFilterRacesReload_ShouldStillReopenClosedLogs()
    {
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher, mockLogWatcher, _, _) = CreateEffectsWithServices(activeLogs: activeLogs, appliedFilter: XmlContainsFilter());

        var watcherCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        mockLogWatcher.RemoveLogAsync(Arg.Any<string>(), Arg.Any<EventLogId>()).Returns(watcherCompletion.Task);

        var closeTasks = new List<Task>();

        mockDispatcher
            .When(d => d.Dispatch(Arg.Any<CloseLogAction>()))
            .Do(callInfo =>
            {
                closeTasks.Add(effects.HandleCloseLog(callInfo.ArgAt<CloseLogAction>(0), mockDispatcher));
            });

        var xmlFilter1 = FilterBuilder.CreateTestFilter(FilterTestConstants.FilterXmlContainsData, isEnabled: true);
        var filter1 = new Filter(null, [xmlFilter1]);

        var applyFilterTask = effects.HandleApplyFilter(new ApplyFilterAction(filter1), mockDispatcher);

        Assert.False(applyFilterTask.IsCompleted,
            "HandleApplyFilter must wait for HandleCloseLog before populating the restore map.");

        var nonXmlFilter = FilterBuilder.CreateTestFilter(isEnabled: true);
        var filter2 = new Filter(null, [nonXmlFilter]);
        await effects.HandleApplyFilter(new ApplyFilterAction(filter2), mockDispatcher);

        watcherCompletion.SetResult();

        await applyFilterTask.WaitAsync(s_testTimeout, TestContext.Current.CancellationToken);

        mockDispatcher.Received(1)
            .Dispatch(Arg.Is<OpenLogAction>(a => a != null &&
                a.LogName == Constants.LogNameTestLog && a.LogPathType == LogPathType.Channel));

        await Task.WhenAll(closeTasks);
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
        await closeTask.WaitAsync(s_testTimeout, TestContext.Current.CancellationToken);

        Assert.True(closeTask.IsCompletedSuccessfully);
        await mockLogWatcher.Received(1).RemoveAllAsync();
    }

    [Fact]
    public async Task HandleCloseAll_ShouldClearAllResolvedXml()
    {
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

        await effects.HandleCloseAll(mockDispatcher);

        mockXmlResolver.Received(1).ClearAll();
    }

    [Fact]
    public async Task HandleCloseAll_ShouldRemoveAllLogsAndClearCache()
    {
        var (effects, mockDispatcher, mockLogWatcher, mockResolverCache, _) = CreateEffectsWithServices();

        await effects.HandleCloseAll(mockDispatcher);

        await mockLogWatcher.Received(1).RemoveAllAsync();
        mockResolverCache.Received(1).ClearAll();
    }

    [Fact]
    public async Task HandleCloseLog_AwaitsWatcherShutdown_BeforeSignalingCloseCompletion()
    {
        var (effects, mockDispatcher, mockLogWatcher, _, _) = CreateEffectsWithServices();

        var watcherTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        mockLogWatcher.RemoveLogAsync(Arg.Any<string>(), Arg.Any<EventLogId>()).Returns(watcherTcs.Task);

        var logId = EventLogId.Create();
        var action = new CloseLogAction(logId, Constants.LogNameTestLog);

        var closeTask = effects.HandleCloseLog(action, mockDispatcher);

        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.False(closeTask.IsCompleted, "HandleCloseLog should be blocked on RemoveLogAsync.");

        watcherTcs.SetResult();

        await closeTask.WaitAsync(s_testTimeout, TestContext.Current.CancellationToken);

        Assert.True(closeTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task HandleCloseLog_ShouldClearResolvedXmlForLog()
    {
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

        await effects.HandleCloseLog(action, mockDispatcher);

        mockXmlResolver.Received(1).ClearXmlCacheForLog(Constants.LogNameTestLog);
    }

    [Fact]
    public async Task HandleCloseLog_ShouldRemoveLogAndDispatchCloseAction()
    {
        var logId = EventLogId.Create();
        var (effects, mockDispatcher, mockLogWatcher, _, _) = CreateEffectsWithServices();
        var action = new CloseLogAction(logId, Constants.LogNameTestLog);

        await effects.HandleCloseLog(action, mockDispatcher);

        await mockLogWatcher.Received(1).RemoveLogAsync(Constants.LogNameTestLog, logId);

        mockDispatcher.Received(1)
            .Dispatch(Arg.Is<Runtime.LogTable.CloseLogAction>(a => a != null &&
                a.LogId == logId));
    }

    [Fact]
    public async Task HandleCloseLog_WhenLastLog_ShouldClearResolverCache()
    {
        var logId = EventLogId.Create();
        var (effects, mockDispatcher, mockLogWatcher, mockResolverCache, _) = CreateEffectsWithServices();
        var action = new CloseLogAction(logId, Constants.LogNameTestLog);

        await effects.HandleCloseLog(action, mockDispatcher);

        mockResolverCache.Received(1).ClearAll();
    }

    [Fact]
    public async Task HandleCloseLog_WhenNotUserInitiated_DoesNotDispatchUserCloseCompleted()
    {
        var logId = EventLogId.Create();
        var (effects, mockDispatcher, _, _, _) = CreateEffectsWithServices();
        var action = new CloseLogAction(logId, Constants.LogNameTestLog);

        await effects.HandleCloseLog(action, mockDispatcher);

        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<LogClosedByUserCompletedAction>());
    }

    [Fact]
    public async Task HandleCloseLog_WhenOtherLogsRemain_ShouldNotClearResolverCache()
    {
        var logData = new EventLogData(Constants.LogNameLog1, LogPathType.Channel);

        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty
            .Add(Constants.LogNameLog1, logData);

        var (effects, mockDispatcher, _, mockResolverCache, _) = CreateEffectsWithServices(activeLogs: activeLogs);
        var closingLogId = EventLogId.Create();
        var action = new CloseLogAction(closingLogId, Constants.LogNameTestLog);

        await effects.HandleCloseLog(action, mockDispatcher);

        mockResolverCache.DidNotReceive().ClearAll();
    }

    [Fact]
    public async Task HandleCloseLog_WhenUserInitiated_DispatchesUserCloseCompletedAfterTerminal()
    {
        var logId = EventLogId.Create();
        var (effects, mockDispatcher, _, _, _) = CreateEffectsWithServices();
        var action = new CloseLogAction(logId, Constants.LogNameTestLog, UserInitiated: true);

        await effects.HandleCloseLog(action, mockDispatcher);

        Received.InOrder(() =>
        {
            mockDispatcher.Dispatch(Arg.Is<Runtime.LogTable.CloseLogAction>(a => a != null && a.LogId == logId));
            mockDispatcher.Dispatch(Arg.Is<LogClosedByUserCompletedAction>(a => a != null && a.LogId == logId));
        });
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

        mockDispatcher.Received(1).Dispatch(Arg.Is<IngestRawEventsAction>(a => a != null &&
            a.Mode == RawIngestMode.Prepend && a.EventsByLog.ContainsKey(logData.Id)));
    }

    [Fact]
    public async Task HandleLoadNewEvents_ShouldProcessBufferAndDispatchActions()
    {
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
        var pending = CaptureDispatchQueue(mockDispatcher);

        await effects.HandleLoadNewEvents(mockDispatcher);
        await DrainDispatchQueueAsync(pending, effects, mockDispatcher, () => rawState, r => rawState = r);

        mockDispatcher.Received(1)
            .Dispatch(Arg.Is<IngestRawEventsAction>(a => a != null &&
                a.EventsByLog.ContainsKey(logData.Id) &&
                a.EventsByLog[logData.Id].Count == 2));

        mockDispatcher.Received(1)
            .Dispatch(Arg.Is<NewEventBufferConsumedAction>(a => a != null && a.ConsumedEvents.Count == 2));
    }

    [Fact]
    public async Task HandleLoadNewEvents_WhenAllEventsFiltered_ShouldNotDispatchAppendBatch()
    {
        var bufferedEvents = new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(100, logName: Constants.LogNameTestLog)
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

        var (effects, mockDispatcher, mockFilterService) = CreateEffectsWithMutableState(() => state, () => rawState);

        mockFilterService.GetFilteredEvents(Arg.Any<IEnumerable<ResolvedEvent>>(), Arg.Any<Filter>())
            .Returns(new List<ResolvedEvent>());

        var pending = CaptureDispatchQueue(mockDispatcher);

        await effects.HandleLoadNewEvents(mockDispatcher);
        await DrainDispatchQueueAsync(pending, effects, mockDispatcher, () => rawState, r => rawState = r);

        mockDispatcher.Received(1)
            .Dispatch(Arg.Is<NewEventBufferConsumedAction>(a => a != null && a.ConsumedEvents.Count == 1));
    }

    [Fact]
    public async Task HandleLoadNewEvents_WhenBufferSpansMultipleLogs_ShouldGroupIntoSingleBatch()
    {
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

        IngestRawEventsAction? captured = null;

        mockDispatcher
            .When(dispatcher => dispatcher.Dispatch(Arg.Any<IngestRawEventsAction>()))
            .Do(call => captured = call.ArgAt<IngestRawEventsAction>(0));

        await effects.HandleLoadNewEvents(mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Any<IngestRawEventsAction>());
        Assert.NotNull(captured);
        Assert.Equal(2, captured.EventsByLog.Count);
        Assert.Equal(2, captured.EventsByLog[applicationLog.Id].Count);
        Assert.Single(captured.EventsByLog[testLog.Id]);
    }

    [Fact]
    public async Task HandleOpenLog_AwaitsInitialClassificationTask_BeforeResolverConstruction()
    {
        var logData = new EventLogData(Constants.LogNameApplication, LogPathType.Channel);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameApplication, logData);

        var classificationTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var (effects, mockDispatcher, mockServiceProvider, _, mockDatabaseService) =
            CreateEffectsForOpenLogGuards(activeLogs);

        mockDatabaseService.InitialClassificationTask.Returns(classificationTcs.Task);

        var action = new OpenLogAction(Constants.LogNameApplication, LogPathType.Channel);

        var openTask = effects.HandleOpenLog(action, mockDispatcher);

        mockServiceProvider.DidNotReceive().GetService(typeof(IEventResolver));

        classificationTcs.SetResult(true);
        await openTask;

        mockServiceProvider.Received(1).GetService(typeof(IEventResolver));
    }

    [Fact]
    public async Task HandleOpenLog_LogClosedDuringClassificationAwait_DoesNotDispatchAddTable()
    {
        var logData = new EventLogData(Constants.LogNameApplication, LogPathType.Channel);

        var classificationTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

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

        var openTask = effects.HandleOpenLog(action, mockDispatcher);

        mockEventLogState.Value.Returns(new EventLogState
        {
            OpenLogs = ImmutableDictionary<string, OpenLogInfo>.Empty,
            AppliedFilter = new Filter(null, [])
        });

        classificationTcs.SetResult(true);
        await openTask;

        mockServiceProvider.DidNotReceive().GetService(typeof(IEventResolver));

        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<AddTableAction>());
    }

    [Fact]
    public async Task HandleOpenLog_ResolverThrows_CallsReportCritical_DoesNotPropagate()
    {
        var logData = new EventLogData(Constants.LogNameApplication, LogPathType.Channel);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameApplication, logData);

        var (effects, mockDispatcher, mockServiceProvider, mockCriticalErrorService, _) =
            CreateEffectsForOpenLogGuards(activeLogs);

        var thrown = new InvalidOperationException("resolver factory failed");
        mockServiceProvider.When(p => p.GetService(typeof(IEventResolver))).Do(_ => throw thrown);

        var action = new OpenLogAction(Constants.LogNameApplication, LogPathType.Channel);

        await effects.HandleOpenLog(action, mockDispatcher);

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

        var partials = AllPartialActions(dispatcher);
        Assert.Single(partials);
        Assert.NotEmpty(partials[0].Events);
    }

    [Fact]
    public async Task HandleOpenLog_ReverseEagerLoad_EmptyLog_SeedsWatcherWithNullBookmark()
    {
        var fakeFactory = new FakeEventLogReaderFactory(new FakeEventLogReader([], newestBookmark: null));

        var (openLog, dispatcher, _) = CreateEagerLoadEffects(fakeFactory);

        await openLog.HandleOpenLog(new OpenLogAction(Constants.LogNameApplication, LogPathType.Channel), dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Is<RegisterLiveTailAction>(a => a != null &&
            a.LogData.Name == Constants.LogNameApplication && a.Bookmark == null));
        Assert.Empty(SingleFinalEvents(dispatcher));
    }

    [Fact]
    public async Task HandleOpenLog_ReverseEagerLoad_FinalListContainsEveryEventOnceSortedDescending()
    {
        const int total = 250;
        var fakeFactory = new FakeEventLogReaderFactory(
            new FakeEventLogReader(BuildReverseBatches(total, batchSize: 30), newestBookmark: "NEWEST"));

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
        var arrivalRecordIds = new List<long?> { 1000, 999, 998, null, 5000, 997, 997, 996 };
        for (long next = 995; arrivalRecordIds.Count < 230; next--) { arrivalRecordIds.Add(next); }

        var fakeFactory = new FakeEventLogReaderFactory(
            new FakeEventLogReader(BuildBatchesFromRecordIds(arrivalRecordIds, batchSize: 30), newestBookmark: "NEWEST"));

        var (openLog, dispatcher, _) = CreateEagerLoadEffects(fakeFactory);

        await openLog.HandleOpenLog(new OpenLogAction(Constants.LogNameApplication, LogPathType.Channel), dispatcher);

        var finalEvents = SingleFinalEvents(dispatcher);
        var finalRecordIds = finalEvents.Select(resolved => resolved.RecordId).ToList();
        Assert.Equal(arrivalRecordIds, finalRecordIds);

        var partialEvents = AllPartialEvents(dispatcher).ToList();
        var partialRecordIds = partialEvents.Select(resolved => resolved.RecordId).ToList();
        Assert.NotEmpty(partialRecordIds);
        Assert.Equal(finalRecordIds.Take(partialRecordIds.Count).ToList(), partialRecordIds);

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
        const int total = 250;
        var fakeFactory = new FakeEventLogReaderFactory(
            new FakeEventLogReader(BuildReverseBatches(total, batchSize: 30), newestBookmark: "NEWEST"));

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

        Assert.Equal(partialIds.OrderByDescending(id => id).ToList(), partialIds);

        var finalIds = SingleFinalEvents(dispatcher).Select(resolved => resolved.RecordId).ToList();
        Assert.Equal(finalIds.Take(partialIds.Count).ToList(), partialIds);
    }

    [Fact]
    public async Task HandleOpenLog_ReverseEagerLoad_SeedsWatcherFromNewestBookmark()
    {
        var fakeFactory = new FakeEventLogReaderFactory(
            new FakeEventLogReader(BuildReverseBatches(50, batchSize: 30), newestBookmark: "NEWEST_BOOKMARK"));

        var (openLog, dispatcher, _) = CreateEagerLoadEffects(fakeFactory);

        await openLog.HandleOpenLog(new OpenLogAction(Constants.LogNameApplication, LogPathType.Channel), dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Is<RegisterLiveTailAction>(a => a != null &&
            a.LogData.Name == Constants.LogNameApplication && a.Bookmark == "NEWEST_BOOKMARK"));

        Assert.True(fakeFactory.ReverseDirectionRequested);
    }

    [Fact]
    public async Task HandleOpenLog_ReverseEagerLoad_SuccessfulLoad_ClearsLoadingStatusOnCompletion()
    {
        var fakeFactory = new FakeEventLogReaderFactory(
            new FakeEventLogReader(BuildReverseBatches(60, batchSize: 30), newestBookmark: "NEWEST"));

        var (openLog, dispatcher, _) = CreateEagerLoadEffects(fakeFactory);

        await openLog.HandleOpenLog(new OpenLogAction(Constants.LogNameApplication, LogPathType.Channel), dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Any<ClearStatusAction>());

        var dispatched = dispatcher.ReceivedCalls().Select(call => call.GetArguments()[0]).ToList();
        var lastLoadingIndex = dispatched.FindLastIndex(action => action is SetEventsLoadingAction);
        var clearIndex = dispatched.FindLastIndex(action => action is ClearStatusAction);
        Assert.True(clearIndex > lastLoadingIndex, "ClearStatusAction must be the terminal loading dispatch.");
    }

    [Fact]
    public async Task HandleOpenLog_ReverseEagerLoad_WhenReadStopsOnError_SurfacesLoadFailureNotFinalLoad()
    {
        const int total = 60;
        var fakeFactory = new FakeEventLogReaderFactory(
            new FakeEventLogReader(BuildReverseBatches(total, batchSize: 30), newestBookmark: "NEWEST")
            {
                LastErrorCode = 5
            });

        var (openLog, dispatcher, _) = CreateEagerLoadEffects(fakeFactory);

        await openLog.HandleOpenLog(new OpenLogAction(Constants.LogNameApplication, LogPathType.Channel), dispatcher);

        dispatcher.Received().Dispatch(Arg.Is<SetResolverStatusAction>(a => a != null &&
            a.ResolverStatus.Contains("Error") && a.ResolverStatus.Contains(Constants.LogNameApplication)));
        Assert.Empty(dispatcher.ReceivedCalls().Select(call => call.GetArguments()[0]).OfType<LoadEventsAction>());
    }

    [Fact]
    public async Task HandleOpenLog_ReverseEagerLoad_WhenReaderInvalid_SurfacesLoadFailureNotEmptyLog()
    {
        var fakeFactory = new FakeEventLogReaderFactory(
            new FakeEventLogReader([], newestBookmark: null) { IsValid = false, OpenErrorCode = 5 });

        var (openLog, dispatcher, watcher) = CreateEagerLoadEffects(fakeFactory);

        await openLog.HandleOpenLog(new OpenLogAction(Constants.LogNameApplication, LogPathType.Channel), dispatcher);

        dispatcher.Received().Dispatch(Arg.Is<SetResolverStatusAction>(a => a != null &&
            a.ResolverStatus.Contains("Error") && a.ResolverStatus.Contains(Constants.LogNameApplication)));
        Assert.Empty(dispatcher.ReceivedCalls().Select(call => call.GetArguments()[0]).OfType<LoadEventsAction>());
        watcher.DidNotReceive().AddLog(Arg.Any<string>(), Arg.Any<EventLogId>(), Arg.Any<string>(), Arg.Any<bool>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<RegisterLiveTailAction>());
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

        mockDispatcher.Received(1).Dispatch(Arg.Is<AddTableAction>(a => a != null && a.LogData.Id == logData.Id));
        mockDispatcher.Received().Dispatch(Arg.Is<CloseLogAction>(a => a != null && a.LogId == logData.Id));
    }

    [Fact]
    public async Task HandleOpenLog_WhenCancelled_ShouldDispatchCloseAndClearStatus()
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

        mockDispatcher.Received().Dispatch(Arg.Any<CloseLogAction>());
        mockDispatcher.Received(1).Dispatch(Arg.Any<ClearStatusAction>());
    }

    [Fact]
    public async Task HandleOpenLog_WhenFileLogNotInOpenLogs_ErrorLeadsWithBasenameAndKeepsFullPath()
    {
        var (effects, mockDispatcher) = CreateEffects(hasEventResolver: true);
        var action = new OpenLogAction(@"C:\logs\Security.evtx", LogPathType.File);

        await effects.HandleOpenLog(action, mockDispatcher);

        mockDispatcher.Received(1)
            .Dispatch(Arg.Is<SetResolverStatusAction>(a => a != null &&
                a.ResolverStatus.StartsWith("Error: Failed to open Security.evtx") &&
                a.ResolverStatus.Contains(@"C:\logs\Security.evtx")));
    }

    [Fact]
    public async Task HandleOpenLog_WhenLogNotInOpenLogs_ShouldDispatchError()
    {
        var (effects, mockDispatcher) = CreateEffects(hasEventResolver: true);
        var action = new OpenLogAction(Constants.LogNameTestLog, LogPathType.Channel);

        await effects.HandleOpenLog(action, mockDispatcher);

        mockDispatcher.Received(1)
            .Dispatch(Arg.Is<SetResolverStatusAction>(a => a != null &&
                a.ResolverStatus.Contains("Error") && a.ResolverStatus.Contains(Constants.LogNameTestLog)));
    }

    [Fact]
    public async Task HandleOpenLog_WhenNoEventResolver_ShouldDispatchError()
    {
        var logData = new EventLogData(Constants.LogNameApplication, LogPathType.Channel);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameApplication, logData);

        var (effects, mockDispatcher) = CreateEffects(
            activeLogs: activeLogs,
            hasEventResolver: false);

        var action = new OpenLogAction(Constants.LogNameApplication, LogPathType.Channel);

        await effects.HandleOpenLog(action, mockDispatcher);

        mockDispatcher.Received(1)
            .Dispatch(Arg.Is<SetResolverStatusAction>(a => a != null &&
                a.ResolverStatus.Contains("Error")));
    }

    [Fact]
    public async Task HandleOpenLog_WhenReaderCreationThrows_ClosesLogAndClearsStatus()
    {
        var (openLog, dispatcher, _) = CreateEagerLoadEffects(new ThrowingReaderFactory());

        await openLog.HandleOpenLog(new OpenLogAction(Constants.LogNameApplication, LogPathType.Channel), dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Any<ClearStatusAction>());
        dispatcher.Received().Dispatch(Arg.Any<CloseLogAction>());
        dispatcher.Received().Dispatch(Arg.Is<SetResolverStatusAction>(action => action != null && action.ResolverStatus.Contains("Error")));
    }

    [Fact]
    public async Task HandleOpenLog_WhenRecordCountAvailable_DispatchesLoadingTotalFromProbe()
    {
        var fakeFactory = new FakeEventLogReaderFactory(
            new FakeEventLogReader(BuildReverseBatches(60, batchSize: 30), newestBookmark: "NEWEST"))
        {
            RecordCount = 10_000
        };

        var (openLog, dispatcher, _) = CreateEagerLoadEffects(fakeFactory);

        // The probe is fire-and-forget; synchronize on the observable dispatch so the assertion is deterministic and
        // this test fails (via WaitAsync timeout) if the effect ever stops invoking the probe.
        var totalDispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatcher.When(target => target.Dispatch(Arg.Any<SetLoadingTotalAction>()))
            .Do(_ => totalDispatched.TrySetResult());

        await openLog.HandleOpenLog(new OpenLogAction(Constants.LogNameApplication, LogPathType.Channel), dispatcher);
        await totalDispatched.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        dispatcher.Received(1).Dispatch(Arg.Is<SetLoadingTotalAction>(action => action != null && action.Total == 10_000));
    }

    [Fact]
    public async Task HandleRegisterLiveTail_WhenLogClosed_DoesNotRegisterWatcher()
    {
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var (effects, _, watcher, _, _) = CreateEffectsWithServices();

        await effects.OpenLog.HandleRegisterLiveTail(
            new RegisterLiveTailAction(logData, "BOOKMARK", RenderXml: true),
            Substitute.For<IDispatcher>());

        watcher.DidNotReceive().AddLog(Arg.Any<string>(), Arg.Any<EventLogId>(), Arg.Any<string>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task HandleRegisterLiveTail_WhenLogReplacedByNewerId_DoesNotRegisterWatcher()
    {
        var openLogData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, openLogData);
        var (effects, _, watcher, _, _) = CreateEffectsWithServices(activeLogs: activeLogs);

        var staleLogData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        Assert.NotEqual(openLogData.Id, staleLogData.Id);

        await effects.OpenLog.HandleRegisterLiveTail(
            new RegisterLiveTailAction(staleLogData, "BOOKMARK", RenderXml: true),
            Substitute.For<IDispatcher>());

        watcher.DidNotReceive().AddLog(Arg.Any<string>(), Arg.Any<EventLogId>(), Arg.Any<string>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task HandleRegisterLiveTail_WhenLogStillOpenWithSameId_RegistersWatcher()
    {
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);
        var (effects, _, watcher, _, _) = CreateEffectsWithServices(activeLogs: activeLogs);

        await effects.OpenLog.HandleRegisterLiveTail(
            new RegisterLiveTailAction(logData, "BOOKMARK", RenderXml: true),
            Substitute.For<IDispatcher>());

        watcher.Received(1).AddLog(Constants.LogNameTestLog, logData.Id, "BOOKMARK", true);
    }

    [Fact]
    public async Task HandleSetContinuouslyUpdate_WhenFalse_ShouldNotProcessBuffer()
    {
        var bufferedEvents = new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(100, logName: Constants.LogNameTestLog)
        };

        var (effects, mockDispatcher) = CreateEffects(newEventBuffer: bufferedEvents);
        var action = new SetContinuouslyUpdateAction(false);

        await effects.HandleSetContinuouslyUpdate(action, mockDispatcher);

        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<IngestRawEventsAction>());
    }

    [Fact]
    public async Task HandleSetContinuouslyUpdate_WhenTrue_ShouldProcessBuffer()
    {
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

        await effects.HandleSetContinuouslyUpdate(action, mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Any<IngestRawEventsAction>());
    }

    [Fact]
    public void ReduceNewEventBufferConsumed_RemovesOnlyCapturedEntriesByReferenceIdentity()
    {
        var eventA = FilterEventBuilder.CreateTestEvent(100, logName: Constants.LogNameTestLog);
        var eventB = FilterEventBuilder.CreateTestEvent(200, logName: Constants.LogNameTestLog);

        var state = new EventLogState { NewEventBuffer = [eventB, eventA] };

        var result = Reducers.ReduceNewEventBufferConsumed(state, new NewEventBufferConsumedAction([eventA]));

        Assert.Single(result.NewEventBuffer);
        Assert.Same(eventB, result.NewEventBuffer[0]);
        Assert.False(result.NewEventBufferIsFull);
    }

    [Fact]
    public void ReduceNewEventBufferConsumed_UsesReferenceIdentity_NotValueEquality()
    {
        var captured = FilterEventBuilder.CreateTestEvent(100, logName: Constants.LogNameTestLog);
        var duplicate = captured with { };
        Assert.Equal(captured, duplicate);
        Assert.NotSame(captured, duplicate);

        var state = new EventLogState { NewEventBuffer = [duplicate, captured] };

        var result = Reducers.ReduceNewEventBufferConsumed(
            state, new NewEventBufferConsumedAction([captured]));

        Assert.Single(result.NewEventBuffer);
        Assert.Same(duplicate, result.NewEventBuffer[0]);
    }

    [Fact]
    public void ReopenAfterDatabaseRemoval_DispatchesOpenLogPerSnapshotEntry()
    {
        var (effects, mockDispatcher, _, _, _) = CreateEffectsWithServices();
        var coordinator = (ILogReloadCoordinator)effects.DatabaseCoordination;

        var snapshot = new[]
        {
            new LogReopenInfo(Constants.LogNameLog1, LogPathType.Channel),
            new LogReopenInfo(Constants.LogNameLog2, LogPathType.File)
        };

        coordinator.ReopenAfterDatabaseRemoval(snapshot);

        mockDispatcher.Received(1)
            .Dispatch(Arg.Is<OpenLogAction>(a => a != null &&
                a.LogName == Constants.LogNameLog1 && a.LogPathType == LogPathType.Channel));

        mockDispatcher.Received(1)
            .Dispatch(Arg.Is<OpenLogAction>(a => a != null &&
                a.LogName == Constants.LogNameLog2 && a.LogPathType == LogPathType.File));
    }

    [Fact]
    public async Task RunRecordCountProbe_WhenGetRecordCountThrows_SwallowsAndDoesNotDispatch()
    {
        var fakeFactory = new FakeEventLogReaderFactory(
            new FakeEventLogReader(BuildReverseBatches(0, batchSize: 30), newestBookmark: null))
        {
            ThrowOnGetRecordCount = true
        };

        var (openLog, dispatcher, _) = CreateEagerLoadEffects(fakeFactory);

        await openLog.RunRecordCountProbeAsync(
            StatusActivityId.Create(), Constants.LogNameApplication, LogPathType.Channel, dispatcher, CancellationToken.None);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<SetLoadingTotalAction>());
    }

    [Fact]
    public async Task RunRecordCountProbe_WithNullCount_DoesNotDispatch()
    {
        var fakeFactory = new FakeEventLogReaderFactory(
            new FakeEventLogReader(BuildReverseBatches(0, batchSize: 30), newestBookmark: null))
        {
            RecordCount = null
        };

        var (openLog, dispatcher, _) = CreateEagerLoadEffects(fakeFactory);

        await openLog.RunRecordCountProbeAsync(
            StatusActivityId.Create(), Constants.LogNameApplication, LogPathType.Channel, dispatcher, CancellationToken.None);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<SetLoadingTotalAction>());
    }

    [Fact]
    public async Task RunRecordCountProbe_WithPositiveCount_DispatchesLoadingTotal()
    {
        var fakeFactory = new FakeEventLogReaderFactory(
            new FakeEventLogReader(BuildReverseBatches(0, batchSize: 30), newestBookmark: null))
        {
            RecordCount = 10_000
        };

        var (openLog, dispatcher, _) = CreateEagerLoadEffects(fakeFactory);
        var activityId = StatusActivityId.Create();

        await openLog.RunRecordCountProbeAsync(
            activityId, Constants.LogNameApplication, LogPathType.Channel, dispatcher, CancellationToken.None);

        Assert.Equal(1, fakeFactory.GetRecordCountCallCount);
        dispatcher.Received(1).Dispatch(
            Arg.Is<SetLoadingTotalAction>(action => action != null && action.ActivityId == activityId && action.Total == 10_000));
    }

    [Fact]
    public async Task RunRecordCountProbe_WithZeroCount_DoesNotDispatch()
    {
        var fakeFactory = new FakeEventLogReaderFactory(
            new FakeEventLogReader(BuildReverseBatches(0, batchSize: 30), newestBookmark: null))
        {
            RecordCount = 0
        };

        var (openLog, dispatcher, _) = CreateEagerLoadEffects(fakeFactory);

        await openLog.RunRecordCountProbeAsync(
            StatusActivityId.Create(), Constants.LogNameApplication, LogPathType.Channel, dispatcher, CancellationToken.None);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<SetLoadingTotalAction>());
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

        var rawStoreForFiltering = Substitute.For<IState<RawEventStoreState>>();
        rawStoreForFiltering.Value.Returns(new RawEventStoreState());

        var filtering = new FilteringEffects(
            eventLogState,
            rawStoreForFiltering,
            new LiveTailIngestCoordinator(dispatcher, Timeout.InfiniteTimeSpan),
            new XmlReloadCoordinator(eventLogState, closeCoordinator, concurrencyState, logger),
            new XmlFilterMatcher(),
            new XmlFilterMatchCache(),
            concurrencyState,
            new ImmediateCpuWorkScheduler(),
            logger);

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
            new LiveTailIngestCoordinator(dispatcher, Timeout.InfiniteTimeSpan),
            new EventLogReaderFactory());

        var logReload = new LogReloadEffects(
            eventLogState,
            rawEventStore,
            closeCoordinator);

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

    private static Queue<object> CaptureDispatchQueue(IDispatcher dispatcher)
    {
        var pending = new Queue<object>();
        dispatcher.When(d => d.Dispatch(Arg.Any<object>())).Do(call => pending.Enqueue(call.ArgAt<object>(0)));
        return pending;
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
            var record = callInfo.ArgAt<EventRecord>(0);

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
        watcher.RemoveLogAsync(Arg.Any<string>(), Arg.Any<EventLogId>()).Returns(Task.CompletedTask);
        watcher.RemoveAllAsync().Returns(Task.CompletedTask);

        var dispatcher = Substitute.For<IDispatcher>();

        var rawEventStore = Substitute.For<IState<RawEventStoreState>>();
        rawEventStore.Value.Returns(new RawEventStoreState());

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
            new LiveTailIngestCoordinator(dispatcher, Timeout.InfiniteTimeSpan),
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
            .Returns(callInfo => callInfo.ArgAt<IEnumerable<ResolvedEvent>>(0).ToList());

        var mockLogger = Substitute.For<ITraceLogger>();
        var mockLogWatcherService = Substitute.For<ILogWatcherService>();
        mockLogWatcherService.RemoveLogAsync(Arg.Any<string>(), Arg.Any<EventLogId>()).Returns(Task.CompletedTask);
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
            .Returns(callInfo => callInfo.ArgAt<IEnumerable<ResolvedEvent>>(0).ToList());

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
            List<ResolvedEvent>? newEventBuffer = null,
            Filter? appliedFilter = null)
    {
        var effectiveActiveLogs = activeLogs ?? ImmutableDictionary<string, EventLogData>.Empty;
        var (openLogs, rawStore) = BuildOpenLogsAndRawStore(effectiveActiveLogs);

        var mockEventLogState = Substitute.For<IState<EventLogState>>();

        mockEventLogState.Value.Returns(new EventLogState
        {
            ContinuouslyUpdate = continuouslyUpdate,
            OpenLogs = openLogs,
            NewEventBuffer = newEventBuffer ?? [],
            AppliedFilter = appliedFilter ?? new Filter(null, [])
        });

        var mockFilterService = Substitute.For<IFilterService>();

        mockFilterService.FilterActiveLogs(Arg.Any<IReadOnlyList<(EventLogId Id, IReadOnlyList<ResolvedEvent> Events)>>(), Arg.Any<Filter>())
            .Returns(new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>>());

        mockFilterService.GetFilteredEvents(Arg.Any<IEnumerable<ResolvedEvent>>(), Arg.Any<Filter>())
            .Returns(callInfo => callInfo.ArgAt<IEnumerable<ResolvedEvent>>(0).ToList());

        var mockLogger = Substitute.For<ITraceLogger>();
        var mockLogWatcherService = Substitute.For<ILogWatcherService>();
        mockLogWatcherService.RemoveLogAsync(Arg.Any<string>(), Arg.Any<EventLogId>()).Returns(Task.CompletedTask);
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

    private static Task DrainDispatchQueueAsync(
        Queue<object> pending,
        EffectsHarness effects,
        IDispatcher dispatcher,
        Func<RawEventStoreState> getRaw,
        Action<RawEventStoreState> setRaw)
    {
        _ = effects;
        _ = dispatcher;

        while (pending.Count > 0)
        {
            if (pending.Dequeue() is IngestRawEventsAction ingest)
            {
                setRaw(RawEventStoreReducers.ReduceIngestRawEvents(getRaw(), ingest));
            }
        }

        return Task.CompletedTask;
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

    private static Filter XmlContainsFilter() =>
        new(null, [FilterBuilder.CreateTestFilter(FilterTestConstants.FilterXmlContainsData, isEnabled: true)]);

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
        public int GetRecordCountCallCount { get; private set; }

        public long? RecordCount { get; set; }

        public bool ReverseDirectionRequested { get; private set; }

        public bool ThrowOnGetRecordCount { get; set; }

        public IEventLogReader CreateReader(string path, LogPathType pathType, bool renderXml = false, bool reverseDirection = false, bool captureSelfDescribing = false)
        {
            ReverseDirectionRequested = reverseDirection;

            return reader;
        }

        public long? GetRecordCount(string path, LogPathType pathType)
        {
            GetRecordCountCallCount++;

            if (ThrowOnGetRecordCount)
            {
                throw new UnauthorizedAccessException("Simulated record-count failure.");
            }

            return RecordCount;
        }
    }

    private sealed class ThrowingReaderFactory : IEventLogReaderFactory
    {
        public IEventLogReader CreateReader(string path, LogPathType pathType, bool renderXml = false, bool reverseDirection = false, bool captureSelfDescribing = false) =>
            throw new InvalidOperationException("Simulated reader creation failure.");

        public long? GetRecordCount(string path, LogPathType pathType) => null;
    }
}
