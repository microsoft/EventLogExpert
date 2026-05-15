// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Logging;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.Resolvers;
using EventLogExpert.UI.Banner;
using EventLogExpert.UI.Database;
using EventLogExpert.UI.EventLog;
using EventLogExpert.UI.Filter;
using EventLogExpert.UI.FilterLoading;
using EventLogExpert.UI.LogTable;
using EventLogExpert.UI.StatusBar;
using EventLogExpert.UI.Tests.TestUtils;
using EventLogExpert.UI.Tests.TestUtils.Constants;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Collections.Immutable;
using CloseAllAction = EventLogExpert.UI.LogTable.CloseAllAction;
using CloseLogAction = EventLogExpert.UI.EventLog.CloseLogAction;
using Effects = EventLogExpert.UI.EventLog.Effects;

namespace EventLogExpert.UI.Tests.EventLog;

public sealed class EffectsTests
{
    [Fact]
    public async Task HandleAddEvent_WhenBufferReachesMaxEvents_ShouldSetFullFlag()
    {
        // Arrange
        var existingBuffer = Enumerable.Range(0, EventLogState.MaxNewEvents - 1)
            .Select(i => EventUtils.CreateTestEvent(i, logName: Constants.LogNameTestLog))
            .ToList();

        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher) = CreateEffects(false, activeLogs, existingBuffer);

        var newEvent = EventUtils.CreateTestEvent(1000, logName: Constants.LogNameTestLog);
        var action = new AddEventAction(newEvent);

        // Act
        await effects.HandleAddEvent(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<AddEventBufferedAction>(a =>
            a.IsFull == true && a.UpdatedBuffer.Count == EventLogState.MaxNewEvents));
    }

    [Fact]
    public async Task HandleAddEvent_WhenContinuouslyUpdateFalse_ShouldBufferEvent()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher) = CreateEffects(
            false,
            activeLogs);

        var newEvent = EventUtils.CreateTestEvent(100, logName: Constants.LogNameTestLog);
        var action = new AddEventAction(newEvent);

        // Act
        await effects.HandleAddEvent(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<AddEventBufferedAction>(a =>
            a.UpdatedBuffer.Count == 1 && a.UpdatedBuffer[0] == newEvent));
    }

    [Fact]
    public async Task HandleAddEvent_WhenContinuouslyUpdateTrue_AndEventFilteredOut_ShouldNotDispatchAppend()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher, _, _, mockFilterService) =
            CreateEffectsWithServices(true, activeLogs);

        // Mock the filter to drop everything (simulate "no events match the active filter").
        mockFilterService.GetFilteredEvents(Arg.Any<IEnumerable<ResolvedEvent>>(), Arg.Any<EventFilter>())
            .Returns(new List<ResolvedEvent>());

        var newEvent = EventUtils.CreateTestEvent(100, logName: Constants.LogNameTestLog);
        var action = new AddEventAction(newEvent);

        // Act
        await effects.HandleAddEvent(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Any<AddEventSuccessAction>());
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<AppendTableEventsAction>());
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<UpdateDisplayedEventsAction>());
    }

    [Fact]
    public async Task HandleAddEvent_WhenContinuouslyUpdateTrue_ShouldDispatchSuccessAndUpdate()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher) = CreateEffects(
            true,
            activeLogs);

        var newEvent = EventUtils.CreateTestEvent(100, logName: Constants.LogNameTestLog);
        var action = new AddEventAction(newEvent);

        // Act
        await effects.HandleAddEvent(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Any<AddEventSuccessAction>());
        mockDispatcher.Received(1).Dispatch(Arg.Is<AppendTableEventsAction>(a =>
            a.LogId == logData.Id && a.Events.Count == 1 && a.Events[0] == newEvent));
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<UpdateDisplayedEventsAction>());
    }

    [Fact]
    public async Task HandleAddEvent_WhenLogNotActive_ShouldNotDispatchActions()
    {
        // Arrange
        var (effects, mockDispatcher) = CreateEffects();
        var newEvent = EventUtils.CreateTestEvent(100, logName: Constants.LogNameTestLog);
        var action = new AddEventAction(newEvent);

        // Act
        await effects.HandleAddEvent(action, mockDispatcher);

        // Assert - No dispatches should occur
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<object>());
    }

    [Fact]
    public async Task HandleCloseAll_DispatchesStateClearsBeforeWatcherDrain()
    {
        var (effects, mockDispatcher, mockLogWatcher, mockResolverCache, _) = CreateEffectsWithServices();

        var watcherTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        mockLogWatcher.RemoveAllAsync().Returns(watcherTcs.Task);

        var closeTask = effects.HandleCloseAll(mockDispatcher);

        Assert.False(closeTask.IsCompleted, "HandleCloseAll must still be awaiting RemoveAllAsync.");
        mockDispatcher.Received(1).Dispatch(Arg.Any<CloseAllAction>());
        mockDispatcher.Received(1).Dispatch(Arg.Any<UI.StatusBar.CloseAllAction>());
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
            ActiveLogs = ImmutableDictionary<string, EventLogData>.Empty,
            AppliedFilter = new EventFilter(null, [])
        });

        var mockXmlResolver = Substitute.For<IEventXmlResolver>();
        var mockServiceScopeFactory = Substitute.For<IServiceScopeFactory>();
        var mockServiceScope = Substitute.For<IServiceScope>();

        mockServiceScopeFactory.CreateScope().Returns(mockServiceScope);
        mockServiceScope.ServiceProvider.Returns(Substitute.For<IServiceProvider>());

        var effects = new Effects(
            mockEventLogState,
            Substitute.For<IFilterService>(),
            Substitute.For<ITraceLogger>(),
            Substitute.For<ILogWatcherService>(),
            Substitute.For<IEventResolverCache>(),
            mockXmlResolver,
            mockServiceScopeFactory,
            Substitute.For<IDatabaseService>(),
            Substitute.For<IBannerService>(),
            Substitute.For<IDispatcher>());

        var mockDispatcher = Substitute.For<IDispatcher>();

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
        mockDispatcher.Received(1).Dispatch(Arg.Any<CloseAllAction>());
        mockDispatcher.Received(1).Dispatch(Arg.Any<UI.StatusBar.CloseAllAction>());
    }

    [Fact]
    public async Task HandleCloseLog_AwaitsWatcherShutdown_BeforeSignalingCloseCompletion()
    {
        // Arrange — RemoveLogAsync returns a TCS we control; HandleCloseLog must NOT
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

        // Assert — close has not completed because the watcher hasn't released yet.
        Assert.False(closeTask.IsCompleted, "HandleCloseLog should be blocked on RemoveLogAsync.");

        // Now release the watcher; HandleCloseLog should complete promptly.
        watcherTcs.SetResult();

        await closeTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.True(closeTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task HandleCloseLog_ShouldClearResolvedXmlForLog()
    {
        // Arrange — verify the IEventXmlResolver entry for the closed log is evicted so a
        // subsequent reopen doesn't return stale text from the previous log instance.
        var logId = EventLogId.Create();
        var mockEventLogState = Substitute.For<IState<EventLogState>>();

        mockEventLogState.Value.Returns(new EventLogState
        {
            ActiveLogs = ImmutableDictionary<string, EventLogData>.Empty,
            AppliedFilter = new EventFilter(null, [])
        });

        var mockXmlResolver = Substitute.For<IEventXmlResolver>();
        var mockServiceScopeFactory = Substitute.For<IServiceScopeFactory>();
        var mockServiceScope = Substitute.For<IServiceScope>();

        mockServiceScopeFactory.CreateScope().Returns(mockServiceScope);
        mockServiceScope.ServiceProvider.Returns(Substitute.For<IServiceProvider>());

        var effects = new Effects(
            mockEventLogState,
            Substitute.For<IFilterService>(),
            Substitute.For<ITraceLogger>(),
            Substitute.For<ILogWatcherService>(),
            Substitute.For<IEventResolverCache>(),
            mockXmlResolver,
            mockServiceScopeFactory,
            Substitute.For<IDatabaseService>(),
            Substitute.For<IBannerService>(),
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

        mockDispatcher.Received(1).Dispatch(Arg.Is<UI.LogTable.CloseLogAction>(a =>
            a.LogId == logId));
    }

    [Fact]
    public async Task HandleCloseLog_WhenLastLog_ShouldClearResolverCache()
    {
        // Arrange — state has no active logs (reducer already removed the last one)
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
        // Arrange — state still has another active log
        var logData = new EventLogData(Constants.LogNameLog1, LogPathType.Channel, []);
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
    public async Task HandleLoadEvents_ShouldFilterAndDispatchUpdateTable()
    {
        // Arrange
        var events = ImmutableArray.Create(
            EventUtils.CreateTestEvent(100, level: Constants.EventLevelError),
            EventUtils.CreateTestEvent(200, level: Constants.EventLevelInformation)
        );

        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);
        var (effects, mockDispatcher, _, _, mockFilterService) = CreateEffectsWithServices();

        var action = new LoadEventsAction(logData, events);

        // Act
        await effects.HandleLoadEvents(action, mockDispatcher);

        // Assert
        mockFilterService.Received(1).GetFilteredEvents(events, Arg.Any<EventFilter>());
        mockDispatcher.Received(1).Dispatch(Arg.Any<UpdateTableAction>());
    }

    [Fact]
    public async Task HandleLoadNewEvents_ShouldProcessBufferAndDispatchActions()
    {
        // Arrange
        var bufferedEvents = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(100, logName: Constants.LogNameTestLog),
            EventUtils.CreateTestEvent(200, logName: Constants.LogNameTestLog)
        };

        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher) = CreateEffects(
            activeLogs: activeLogs,
            newEventBuffer: bufferedEvents);

        // Act
        await effects.HandleLoadNewEvents(mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<AppendTableEventsBatchAction>(a =>
            a.EventsByLog.Count == 1 &&
            a.EventsByLog.ContainsKey(logData.Id) &&
            a.EventsByLog[logData.Id].Count == 2));
        mockDispatcher.Received(1).Dispatch(Arg.Any<AddEventSuccessAction>());
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<UpdateDisplayedEventsAction>());

        mockDispatcher.Received(1).Dispatch(Arg.Is<AddEventBufferedAction>(a =>
            a.UpdatedBuffer.Count == 0 && a.IsFull == false));
    }

    [Fact]
    public async Task HandleLoadNewEvents_WhenAllEventsFiltered_ShouldNotDispatchAppendBatch()
    {
        // Arrange
        var bufferedEvents = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(100, logName: Constants.LogNameTestLog)
        };

        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher, _, _, mockFilterService) =
            CreateEffectsWithServices(activeLogs: activeLogs, newEventBuffer: bufferedEvents);

        mockFilterService.GetFilteredEvents(Arg.Any<IEnumerable<ResolvedEvent>>(), Arg.Any<EventFilter>())
            .Returns(new List<ResolvedEvent>());

        // Act
        await effects.HandleLoadNewEvents(mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Any<AddEventSuccessAction>());
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<AppendTableEventsBatchAction>());
        mockDispatcher.Received(1).Dispatch(Arg.Is<AddEventBufferedAction>(a =>
            a.UpdatedBuffer.Count == 0 && a.IsFull == false));
    }

    [Fact]
    public async Task HandleLoadNewEvents_WhenBufferSpansMultipleLogs_ShouldGroupIntoSingleBatch()
    {
        // Arrange
        // EventUtils.CreateTestEvent always sets OwningLog="TestLog"; override via `with` to span 2 logs.
        var bufferedEvents = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(100) with { OwningLog = Constants.LogNameApplication },
            EventUtils.CreateTestEvent(200) with { OwningLog = Constants.LogNameTestLog },
            EventUtils.CreateTestEvent(300) with { OwningLog = Constants.LogNameApplication }
        };

        var applicationLog = new EventLogData(Constants.LogNameApplication, LogPathType.Channel, []);
        var testLog = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);

        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty
            .Add(Constants.LogNameApplication, applicationLog)
            .Add(Constants.LogNameTestLog, testLog);

        var (effects, mockDispatcher) = CreateEffects(
            activeLogs: activeLogs,
            newEventBuffer: bufferedEvents);

        // Capture the dispatched batch for inspection.
        AppendTableEventsBatchAction? captured = null;

        mockDispatcher
            .When(dispatcher => dispatcher.Dispatch(Arg.Any<AppendTableEventsBatchAction>()))
            .Do(call => captured = (AppendTableEventsBatchAction)call.Args()[0]);

        // Act
        await effects.HandleLoadNewEvents(mockDispatcher);

        // Assert: a single batched append covering both logs.
        mockDispatcher.Received(1).Dispatch(Arg.Any<AppendTableEventsBatchAction>());
        Assert.NotNull(captured);
        Assert.Equal(2, captured.EventsByLog.Count);
        Assert.Equal(2, captured.EventsByLog[applicationLog.Id].Count);
        Assert.Single(captured.EventsByLog[testLog.Id]);
    }

    [Fact]
    public async Task HandleOpenLog_AwaitsInitialClassificationTask_BeforeResolverConstruction()
    {
        // Arrange — block classification with a pending TCS so we can verify the resolver
        // is NOT looked up until classification completes. Determinism comes from the await
        // semantics: the IServiceProvider mock cannot be invoked while the await is parked
        // on an incomplete task, so an immediate post-call assertion is sufficient.
        var logData = new EventLogData(Constants.LogNameApplication, LogPathType.Channel, []);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameApplication, logData);

        var classificationTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var (effects, mockDispatcher, mockServiceProvider, _, mockDatabaseService) =
            CreateEffectsForOpenLogGuards(activeLogs);

        mockDatabaseService.InitialClassificationTask.Returns(classificationTcs.Task);

        var action = new OpenLogAction(Constants.LogNameApplication, LogPathType.Channel);

        // Act 1 — start the open; await yields back at InitialClassificationTask.
        var openTask = effects.HandleOpenLog(action, mockDispatcher);

        // Assert 1 — resolver lookup MUST NOT have been touched yet.
        mockServiceProvider.DidNotReceive().GetService(typeof(IEventResolver));

        // Act 2 — release classification; let HandleOpenLog finish.
        classificationTcs.SetResult(true);
        await openTask;

        // Assert 2 — resolver lookup happens after classification.
        mockServiceProvider.Received(1).GetService(typeof(IEventResolver));
    }

    [Fact]
    public async Task HandleOpenLog_LogClosedDuringClassificationAwait_DoesNotDispatchAddTable()
    {
        // Arrange — simulate the user closing the log (HandleCloseLog dispatch already removed
        // it from ActiveLogs and canceled its CTS) while HandleOpenLog is parked on the
        // classification await. After the await releases, HandleOpenLog must bail BEFORE
        // calling LoadLogAsync — otherwise LoadLogAsync's AddTable dispatch would resurrect
        // a table entry the user already dismissed, leaving an orphan in LogTableState.
        var logData = new EventLogData(Constants.LogNameApplication, LogPathType.Channel, []);
        var initialActiveLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameApplication, logData);

        var classificationTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Use a mutable IState so we can flip ActiveLogs to "log closed" partway through.
        var mockEventLogState = Substitute.For<IState<EventLogState>>();
        var initialState = new EventLogState
        {
            ActiveLogs = initialActiveLogs,
            AppliedFilter = new EventFilter(null, [])
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

        var effects = new Effects(
            mockEventLogState,
            Substitute.For<IFilterService>(),
            Substitute.For<ITraceLogger>(),
            Substitute.For<ILogWatcherService>(),
            Substitute.For<IEventResolverCache>(),
            Substitute.For<IEventXmlResolver>(),
            mockServiceScopeFactory,
            mockDatabaseService,
            Substitute.For<IBannerService>(),
            Substitute.For<IDispatcher>());

        var mockDispatcher = Substitute.For<IDispatcher>();
        var action = new OpenLogAction(Constants.LogNameApplication, LogPathType.Channel);

        // Act 1 — start the open; await yields back at InitialClassificationTask.
        var openTask = effects.HandleOpenLog(action, mockDispatcher);

        // Act 2 — simulate HandleCloseLog: remove the log from ActiveLogs.
        mockEventLogState.Value.Returns(new EventLogState
        {
            ActiveLogs = ImmutableDictionary<string, EventLogData>.Empty,
            AppliedFilter = new EventFilter(null, [])
        });

        // Act 3 — release classification; HandleOpenLog should detect the missing log and bail.
        classificationTcs.SetResult(true);
        await openTask;

        // Assert — neither AddTable (would orphan in LogTableState) nor any resolver work
        // happened. The bail-out path returns silently after the post-await identity check.
        mockServiceProvider.DidNotReceive().GetService(typeof(IEventResolver));

        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<AddTableAction>());
    }

    [Fact]
    public async Task HandleOpenLog_ResolverThrows_CallsReportCritical_DoesNotPropagate()
    {
        // Arrange — resolver factory throws (e.g., DI graph misconfiguration). HandleOpenLog
        // must surface this as a Reload-tier banner via IBannerService.ReportCritical and
        // return cleanly instead of letting the exception escape the effect.
        var logData = new EventLogData(Constants.LogNameApplication, LogPathType.Channel, []);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameApplication, logData);

        var (effects, mockDispatcher, mockServiceProvider, mockBannerService, _) =
            CreateEffectsForOpenLogGuards(activeLogs);

        var thrown = new InvalidOperationException("resolver factory failed");
        mockServiceProvider.When(p => p.GetService(typeof(IEventResolver))).Do(_ => throw thrown);

        var action = new OpenLogAction(Constants.LogNameApplication, LogPathType.Channel);

        // Act — must not throw.
        await effects.HandleOpenLog(action, mockDispatcher);

        // Assert — exact exception forwarded to banner; no resolver-status dispatch fired.
        mockBannerService.Received(1).ReportCritical(thrown);

        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<SetResolverStatusAction>());
    }

    [Fact]
    public async Task HandleOpenLog_WhenCancelled_ShouldDispatchCloseAndClearStatus()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameApplication, LogPathType.Channel, []);
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
    public async Task HandleOpenLog_WhenLogNotInActiveLogs_ShouldDispatchError()
    {
        // Arrange
        var (effects, mockDispatcher) = CreateEffects(hasEventResolver: true);
        var action = new OpenLogAction(Constants.LogNameTestLog, LogPathType.Channel);

        // Act
        await effects.HandleOpenLog(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<SetResolverStatusAction>(a =>
            a.ResolverStatus.Contains("Error") && a.ResolverStatus.Contains(Constants.LogNameTestLog)));
    }

    [Fact]
    public async Task HandleOpenLog_WhenNoEventResolver_ShouldDispatchError()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameApplication, LogPathType.Channel, []);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameApplication, logData);

        var (effects, mockDispatcher) = CreateEffects(
            activeLogs: activeLogs,
            hasEventResolver: false);

        var action = new OpenLogAction(Constants.LogNameApplication, LogPathType.Channel);

        // Act
        await effects.HandleOpenLog(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<SetResolverStatusAction>(a =>
            a.ResolverStatus.Contains("Error")));
    }

    [Fact]
    public async Task HandleSetContinuouslyUpdate_WhenFalse_ShouldNotProcessBuffer()
    {
        // Arrange
        var bufferedEvents = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(100, logName: Constants.LogNameTestLog)
        };

        var (effects, mockDispatcher) = CreateEffects(newEventBuffer: bufferedEvents);
        var action = new SetContinuouslyUpdateAction(false);

        // Act
        await effects.HandleSetContinuouslyUpdate(action, mockDispatcher);

        // Assert
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<UpdateDisplayedEventsAction>());
    }

    [Fact]
    public async Task HandleSetContinuouslyUpdate_WhenTrue_ShouldProcessBuffer()
    {
        // Arrange
        var bufferedEvents = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(100, logName: Constants.LogNameTestLog)
        };

        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher) = CreateEffects(
            activeLogs: activeLogs,
            newEventBuffer: bufferedEvents);

        var action = new SetContinuouslyUpdateAction(true);

        // Act
        await effects.HandleSetContinuouslyUpdate(action, mockDispatcher);

        // Assert: ProcessNewEventBuffer now dispatches a batched append (no UpdateDisplayedEvents).
        mockDispatcher.Received(1).Dispatch(Arg.Any<AppendTableEventsBatchAction>());
        mockDispatcher.Received(1).Dispatch(Arg.Any<AddEventSuccessAction>());
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<UpdateDisplayedEventsAction>());
    }

    [Fact]
    public async Task HandleSetFilters_FilterBranch_ShouldBracketDisplayedEventsUpdateWithFilterLoading()
    {
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher, _, _, _) = CreateEffectsWithServices(activeLogs: activeLogs);

        var filter = FilterUtils.CreateTestFilter(isEnabled: true);
        var action = new SetFiltersAction(new EventFilter(null, [filter]));

        await effects.HandleSetFilters(action, mockDispatcher);

        Received.InOrder(() =>
        {
            mockDispatcher.Dispatch(Arg.Is<SetFilterLoadingAction>(a => a.IsLoading));
            mockDispatcher.Dispatch(Arg.Any<UpdateDisplayedEventsAction>());
            mockDispatcher.Dispatch(Arg.Is<SetFilterLoadingAction>(a => !a.IsLoading));
        });
    }

    [Fact]
    public async Task HandleSetFilters_FilterBranch_WhenFilterServiceThrows_ShouldStillClearFilterLoading()
    {
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher, _, _, mockFilterService) =
            CreateEffectsWithServices(activeLogs: activeLogs);

        mockFilterService
            .When(x => x.FilterActiveLogs(Arg.Any<IEnumerable<EventLogData>>(), Arg.Any<EventFilter>()))
            .Do(_ => throw new InvalidOperationException("boom"));

        var filter = FilterUtils.CreateTestFilter(isEnabled: true);
        var action = new SetFiltersAction(new EventFilter(null, [filter]));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => effects.HandleSetFilters(action, mockDispatcher));

        mockDispatcher.Received(1).Dispatch(Arg.Is<SetFilterLoadingAction>(a => a.IsLoading));
        mockDispatcher.Received(1).Dispatch(Arg.Is<SetFilterLoadingAction>(a => !a.IsLoading));
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<UpdateDisplayedEventsAction>());
    }

    [Fact]
    public async Task HandleSetFilters_FilterBranch_WhenLogClosedDuringFilter_ShouldOmitStaleSliceFromDispatch()
    {
        // Arrange — log closes (ActiveLogs.Remove) while the filter task is running. The
        // post-filter dispatch must omit the closed log's slice. (The reducer also skips
        // unknown log ids, but checking at the effect keeps the dispatch minimal.)
        var snapshotEvents = new List<ResolvedEvent> { EventUtils.CreateTestEvent(100) };
        var snapshotData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, snapshotEvents);

        var snapshotState = new EventLogState
        {
            ContinuouslyUpdate = false,
            ActiveLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, snapshotData),
            NewEventBuffer = [],
            AppliedFilter = new EventFilter(null, [])
        };

        var postCloseState = snapshotState with
        {
            ActiveLogs = ImmutableDictionary<string, EventLogData>.Empty
        };

        EventLogState volatileState = snapshotState;

        var filterResult = new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>>
        {
            [snapshotData.Id] = snapshotEvents
        };

        var (effects, mockDispatcher, mockFilterService) = CreateEffectsWithMutableState(() => volatileState);

        mockFilterService.FilterActiveLogs(Arg.Any<IEnumerable<EventLogData>>(), Arg.Any<EventFilter>())
            .Returns(_ =>
            {
                volatileState = postCloseState;
                return filterResult;
            });

        var nonXmlFilter = FilterUtils.CreateTestFilter(isEnabled: true);
        var action = new SetFiltersAction(new EventFilter(null, [nonXmlFilter]));

        // Act
        await effects.HandleSetFilters(action, mockDispatcher);

        // Assert — dispatched UpdateDisplayedEvents has no entry for the closed log id.
        mockDispatcher.Received(1).Dispatch(Arg.Is<UpdateDisplayedEventsAction>(
            a => !a.ActiveLogs.ContainsKey(snapshotData.Id)));
    }

    [Fact]
    public async Task HandleSetFilters_FilterBranch_WhenLogEventsChangeDuringFilter_ShouldRefilterFromCurrentState()
    {
        // Arrange — single open log; live event arrives during the first filter pass (Events ref
        // changes). The new filter must be re-applied to the post-mutation rows in a single retry
        // pass so the user sees the filter applied to the updated row set, not stale rows.
        var snapshotEvents = new List<ResolvedEvent> { EventUtils.CreateTestEvent(100) };
        var snapshotData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, snapshotEvents);

        var snapshotState = new EventLogState
        {
            ContinuouslyUpdate = false,
            ActiveLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, snapshotData),
            NewEventBuffer = [],
            AppliedFilter = new EventFilter(null, [])
        };

        var liveTailEvents = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(100),
            EventUtils.CreateTestEvent(101)
        };

        var postLiveTailState = snapshotState with
        {
            ActiveLogs = snapshotState.ActiveLogs.SetItem(
                Constants.LogNameTestLog,
                snapshotData with { Events = liveTailEvents })
        };

        EventLogState volatileState = snapshotState;

        var pass1Result = new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>>
        {
            [snapshotData.Id] = snapshotEvents
        };

        var pass2Result = new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>>
        {
            [snapshotData.Id] = liveTailEvents
        };

        var (effects, mockDispatcher, mockFilterService) = CreateEffectsWithMutableState(() => volatileState);

        mockFilterService.FilterActiveLogs(Arg.Any<IEnumerable<EventLogData>>(), Arg.Any<EventFilter>())
            .Returns(
                _ =>
                {
                    volatileState = postLiveTailState;
                    return pass1Result;
                },
                _ => pass2Result);

        var nonXmlFilter = FilterUtils.CreateTestFilter(isEnabled: true);
        var action = new SetFiltersAction(new EventFilter(null, [nonXmlFilter]));

        // Act
        await effects.HandleSetFilters(action, mockDispatcher);

        // Assert — FilterActiveLogs ran twice; the second call received the post-mutation
        // EventLogData (proves pass 2 actually re-filtered from current state, not from the
        // pass-1 snapshot). Dispatch contains the re-filtered slice, not the stale pass-1 result.
        mockFilterService.Received(2).FilterActiveLogs(
            Arg.Any<IEnumerable<EventLogData>>(),
            Arg.Any<EventFilter>());

        mockFilterService.Received(1).FilterActiveLogs(
            Arg.Is<IEnumerable<EventLogData>>(logs => logs.Any(l => ReferenceEquals(l.Events, liveTailEvents))),
            Arg.Any<EventFilter>());

        mockDispatcher.Received(1).Dispatch(Arg.Is<UpdateDisplayedEventsAction>(
            a => a.ActiveLogs.ContainsKey(snapshotData.Id)
                && ReferenceEquals(a.ActiveLogs[snapshotData.Id], liveTailEvents)));
    }

    [Fact]
    public async Task HandleSetFilters_FilterBranch_WhenLogStillStaleAfterRetry_ShouldOmitStaleSliceFromDispatch()
    {
        // Arrange — events change during pass 1 AND again during pass 2. Single-retry semantics
        // mean the pass-2 result is still stale; the slice must be omitted so the reducer's
        // preserve-omitted fallback keeps the existing rows (avoids losing live events).
        var snapshotEvents = new List<ResolvedEvent>();
        var snapshotData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, snapshotEvents);

        var snapshotState = new EventLogState
        {
            ContinuouslyUpdate = false,
            ActiveLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, snapshotData),
            NewEventBuffer = [],
            AppliedFilter = new EventFilter(null, [])
        };

        var pass1MutationEvents = new List<ResolvedEvent> { EventUtils.CreateTestEvent(100) };
        var pass2MutationEvents = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(100),
            EventUtils.CreateTestEvent(101)
        };

        var afterPass1State = snapshotState with
        {
            ActiveLogs = snapshotState.ActiveLogs.SetItem(
                Constants.LogNameTestLog,
                snapshotData with { Events = pass1MutationEvents })
        };

        var afterPass2State = snapshotState with
        {
            ActiveLogs = snapshotState.ActiveLogs.SetItem(
                Constants.LogNameTestLog,
                snapshotData with { Events = pass2MutationEvents })
        };

        EventLogState volatileState = snapshotState;

        var pass1Result = new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>>
        {
            [snapshotData.Id] = []
        };

        var pass2Result = new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>>
        {
            [snapshotData.Id] = pass1MutationEvents
        };

        var (effects, mockDispatcher, mockFilterService) = CreateEffectsWithMutableState(() => volatileState);

        mockFilterService.FilterActiveLogs(Arg.Any<IEnumerable<EventLogData>>(), Arg.Any<EventFilter>())
            .Returns(
                _ =>
                {
                    volatileState = afterPass1State;
                    return pass1Result;
                },
                _ =>
                {
                    volatileState = afterPass2State;
                    return pass2Result;
                });

        var nonXmlFilter = FilterUtils.CreateTestFilter(isEnabled: true);
        var action = new SetFiltersAction(new EventFilter(null, [nonXmlFilter]));

        // Act
        await effects.HandleSetFilters(action, mockDispatcher);

        // Assert — both filter passes ran; dispatch omits the still-stale log id.
        mockFilterService.Received(2).FilterActiveLogs(
            Arg.Any<IEnumerable<EventLogData>>(),
            Arg.Any<EventFilter>());

        mockDispatcher.Received(1).Dispatch(Arg.Is<UpdateDisplayedEventsAction>(
            a => !a.ActiveLogs.ContainsKey(snapshotData.Id)));
    }

    [Fact]
    public async Task HandleSetFilters_FilterBranch_WhenSupersededByNewerFilter_ShouldDropStaleResults()
    {
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher, _, _, mockFilterService) =
            CreateEffectsWithServices(activeLogs: activeLogs);

        var staleResult = new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>>
        {
            [logData.Id] = new List<ResolvedEvent> { EventUtils.CreateTestEvent(999) }
        };

        var freshResult = new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>>
        {
            [logData.Id] = new List<ResolvedEvent> { EventUtils.CreateTestEvent(100) }
        };

        var staleGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var staleStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var staleFilterModel = FilterUtils.CreateTestFilter(Constants.FilterIdEquals999, isEnabled: true);
        var freshFilterModel = FilterUtils.CreateTestFilter(isEnabled: true);

        var staleFilter = new EventFilter(null, [staleFilterModel]);
        var freshFilter = new EventFilter(null, [freshFilterModel]);

        mockFilterService
            .FilterActiveLogs(Arg.Any<IEnumerable<EventLogData>>(), Arg.Any<EventFilter>())
            .Returns(callInfo =>
            {
                var filter = callInfo.Arg<EventFilter>();

                if (filter.Filters.Count > 0 && filter.Filters[0].ComparisonText == Constants.FilterIdEquals999)
                {
                    staleStarted.TrySetResult(true);
                    staleGate.Task.GetAwaiter().GetResult();
                    return staleResult;
                }

                return freshResult;
            });

        // Start the stale filter and wait until it is parked inside FilterActiveLogs.
        var staleTask = effects.HandleSetFilters(new SetFiltersAction(staleFilter), mockDispatcher);
        await staleStarted.Task;

        // While the stale filter is still parked, run the fresh filter to completion. This
        // bumps _filterGeneration so the stale run will be detected as superseded.
        await effects.HandleSetFilters(new SetFiltersAction(freshFilter), mockDispatcher);

        // Release the stale filter — its post-await checks should now fail the generation guard.
        staleGate.SetResult(true);
        await staleTask;

        // Fresh result reached the table; stale result was dropped.
        mockDispatcher.Received(1).Dispatch(Arg.Is<UpdateDisplayedEventsAction>(
            a => a.ActiveLogs[logData.Id][0].Id == 100));

        mockDispatcher.DidNotReceive().Dispatch(Arg.Is<UpdateDisplayedEventsAction>(
            a => a.ActiveLogs.ContainsKey(logData.Id) && a.ActiveLogs[logData.Id].Any(e => e.Id == 999)));

        // SetFilterLoading(false) was dispatched by the fresh run; the stale run's finally was
        // suppressed by the generation guard, so we should see exactly one false-dispatch.
        mockDispatcher.Received(1).Dispatch(Arg.Is<SetFilterLoadingAction>(a => !a.IsLoading));
    }

    [Fact]
    public async Task HandleSetFilters_ReloadBranch_ShouldNotDispatchFilterLoading()
    {
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher, _, _, _) = CreateEffectsWithServices(activeLogs: activeLogs);

        var xmlFilter = FilterUtils.CreateTestFilter(Constants.FilterXmlContainsData, isEnabled: true);
        var action = new SetFiltersAction(new EventFilter(null, [xmlFilter]));

        await effects.HandleSetFilters(action, mockDispatcher);

        Assert.True(action.EventFilter.RequiresXml);
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<SetFilterLoadingAction>());
    }

    [Fact]
    public async Task HandleSetFilters_ShouldFilterAndDispatchUpdate()
    {
        // Arrange
        var events = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(100, level: Constants.EventLevelError),
            EventUtils.CreateTestEvent(200, level: Constants.EventLevelInformation)
        };

        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, events);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher, _, _, mockFilterService) = CreateEffectsWithServices(activeLogs: activeLogs);

        var eventFilter = new EventFilter(null, []);
        var action = new SetFiltersAction(eventFilter);

        // Act
        await effects.HandleSetFilters(action, mockDispatcher);

        // Assert
        mockFilterService.Received(1).FilterActiveLogs(
            Arg.Any<IEnumerable<EventLogData>>(),
            eventFilter);

        mockDispatcher.Received(1).Dispatch(Arg.Any<UpdateDisplayedEventsAction>());
    }

    [Fact]
    public async Task HandleSetFilters_WhenFilterDoesNotRequireXml_ShouldNotReloadLogs()
    {
        // Arrange — single active log + non-XML filter (Id-based). RequiresXml should be false,
        // so HandleSetFilters should fall through to UpdateDisplayedEvents and never close/open logs.
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher, _, _, _) = CreateEffectsWithServices(activeLogs: activeLogs);

        var nonXmlFilter = FilterUtils.CreateTestFilter(isEnabled: true);
        var eventFilter = new EventFilter(null, [nonXmlFilter]);
        var action = new SetFiltersAction(eventFilter);

        // Act
        await effects.HandleSetFilters(action, mockDispatcher);

        // Assert — UpdateDisplayedEvents path; no Close/Open dispatches.
        Assert.False(eventFilter.RequiresXml);
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<CloseLogAction>());
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<OpenLogAction>());
        mockDispatcher.Received(1).Dispatch(Arg.Any<UpdateDisplayedEventsAction>());
    }

    [Fact]
    public async Task HandleSetFilters_WhenFilterRequiresXml_AwaitsCloseCompletionBeforeReturning()
    {
        // Arrange — RemoveLogAsync is gated by a controlled TCS so HandleCloseLog cannot
        // complete until the test signals it. HandleCloseLog clears _pendingSelectionRestore
        // for the log name AFTER awaiting the watcher, then signals the close-completion
        // TCS in its finally block. HandleSetFilters must await that close-completion TCS
        // before populating _pendingSelectionRestore — otherwise the in-flight close wipes
        // the freshly-written entry. This test pins down the ordering invariant.
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);
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

        var xmlFilter = FilterUtils.CreateTestFilter(Constants.FilterXmlContainsData, isEnabled: true);
        var eventFilter = new EventFilter(null, [xmlFilter]);

        // Act — start HandleSetFilters; it must remain pending until watcherCompletion fires.
        var setFiltersTask = effects.HandleSetFilters(new SetFiltersAction(eventFilter), mockDispatcher);

        // Assert — task is blocked because HandleCloseLog can't finish until RemoveLogAsync
        // returns. Without the close-completion await in HandleSetFilters, the task would
        // already be completed (it would have written the restore map and returned).
        Assert.False(setFiltersTask.IsCompleted, "HandleSetFilters must wait for HandleCloseLog before populating the restore map.");

        // Release HandleCloseLog → finally block signals the close-completion TCS.
        watcherCompletion.SetResult();

        await setFiltersTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // OpenLog should now have been dispatched (only happens after the close await).
        mockDispatcher.Received(1).Dispatch(Arg.Is<OpenLogAction>(a =>
            a.LogName == Constants.LogNameTestLog && a.LogPathType == LogPathType.Channel));

        // Surface any HandleCloseLog faults before exiting the test.
        await Task.WhenAll(closeTasks);
    }

    [Fact]
    public async Task HandleSetFilters_WhenFilterRequiresXml_ShouldRestoreSelectionAfterReload()
    {
        // Arrange — active log with one previously-selected event (RecordId=42). After the
        // XML filter triggers a reload, HandleLoadEvents should consume the pending restore
        // entry and dispatch SelectEvents with the matching event from the new event set.
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var selectedEvent = EventUtils.CreateTestEvent(100, recordId: 42, logName: Constants.LogNameTestLog);

        var mockEventLogState = Substitute.For<IState<EventLogState>>();

        mockEventLogState.Value.Returns(new EventLogState
        {
            ActiveLogs = activeLogs,
            SelectedEvents = [selectedEvent],
            AppliedFilter = new EventFilter(null, [])
        });

        var mockFilterService = Substitute.For<IFilterService>();

        mockFilterService.GetFilteredEvents(Arg.Any<IEnumerable<ResolvedEvent>>(), Arg.Any<EventFilter>())
            .Returns(callInfo => callInfo.Arg<IEnumerable<ResolvedEvent>>().ToList());

        var mockServiceScopeFactory = Substitute.For<IServiceScopeFactory>();
        var mockServiceScope = Substitute.For<IServiceScope>();
        var mockServiceProvider = Substitute.For<IServiceProvider>();

        mockServiceScopeFactory.CreateScope().Returns(mockServiceScope);
        mockServiceScope.ServiceProvider.Returns(mockServiceProvider);

        var effects = new Effects(
            mockEventLogState,
            mockFilterService,
            Substitute.For<ITraceLogger>(),
            Substitute.For<ILogWatcherService>(),
            Substitute.For<IEventResolverCache>(),
            Substitute.For<IEventXmlResolver>(),
            mockServiceScopeFactory,
            Substitute.For<IDatabaseService>(),
            Substitute.For<IBannerService>(),
            Substitute.For<IDispatcher>());

        var mockDispatcher = Substitute.For<IDispatcher>();

        // Route CloseLog dispatches into HandleCloseLog so the close-completion TCSes
        // get signaled. HandleCloseLog clears _pendingSelectionRestore for the log name as
        // part of its async cleanup; HandleSetFilters must await the close before writing
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

        var xmlFilter = FilterUtils.CreateTestFilter(Constants.FilterXmlContainsData, isEnabled: true);
        var eventFilter = new EventFilter(null, [xmlFilter]);

        // Act 1: Apply the XML filter — populates _pendingSelectionRestore for "TestLog".
        await effects.HandleSetFilters(new SetFiltersAction(eventFilter), mockDispatcher);

        // Act 2: Simulate the subsequent LoadEvents that the new OpenLog produces. The
        // reloaded events include the previously-selected RecordId=42 plus an unrelated one.
        var reloadedEvents = ImmutableArray.Create(
            EventUtils.CreateTestEvent(100, recordId: 42, logName: Constants.LogNameTestLog),
            EventUtils.CreateTestEvent(200, recordId: 99, logName: Constants.LogNameTestLog));

        await effects.HandleLoadEvents(new LoadEventsAction(logData, reloadedEvents), mockDispatcher);

        // Assert — SetSelectedEvents dispatched with exactly the restored event (RecordId=42).
        mockDispatcher.Received(1).Dispatch(Arg.Is<SetSelectedEventsAction>(a =>
            a.SelectedEvents.Count() == 1 && a.SelectedEvents.First().RecordId == 42));

        // Surface any HandleCloseLog faults before exiting the test.
        await Task.WhenAll(closeTasks);
    }

    [Fact]
    public async Task HandleSetFilters_WhenFilterRequiresXmlAndLogLacksXml_ShouldCloseAndReopenLog()
    {
        // Arrange — active log has not been loaded with XML, so it must be re-read.
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher, _, _, _) = CreateEffectsWithServices(activeLogs: activeLogs);

        // Route CloseLog → HandleCloseLog so HandleSetFilters' await on the close-completion
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

        var xmlFilter = FilterUtils.CreateTestFilter(Constants.FilterXmlContainsData, isEnabled: true);
        var eventFilter = new EventFilter(null, [xmlFilter]);
        var action = new SetFiltersAction(eventFilter);

        // Act
        await effects.HandleSetFilters(action, mockDispatcher);

        // Assert
        Assert.True(eventFilter.RequiresXml);

        mockDispatcher.Received(1).Dispatch(Arg.Is<CloseLogAction>(a =>
            a.LogName == Constants.LogNameTestLog && a.LogId == logData.Id));

        mockDispatcher.Received(1).Dispatch(Arg.Is<OpenLogAction>(a =>
            a.LogName == Constants.LogNameTestLog && a.LogPathType == LogPathType.Channel));

        // Reload path returns early — no UpdateDisplayedEvents until LoadEvents fires.
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<UpdateDisplayedEventsAction>());

        // Surface any HandleCloseLog faults before exiting the test.
        await Task.WhenAll(closeTasks);
    }

    [Fact]
    public void ReopenAfterDatabaseRemoval_DispatchesOpenLogPerSnapshotEntry()
    {
        // Arrange
        var (effects, mockDispatcher, _, _, _) = CreateEffectsWithServices();
        var coordinator = (ILogReloadCoordinator)effects;

        var snapshot = new[]
        {
            new LogReopenInfo(Constants.LogNameLog1, LogPathType.Channel),
            new LogReopenInfo(Constants.LogNameLog2, LogPathType.File)
        };

        // Act
        coordinator.ReopenAfterDatabaseRemoval(snapshot);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<OpenLogAction>(a =>
            a.LogName == Constants.LogNameLog1 && a.LogPathType == LogPathType.Channel));
        mockDispatcher.Received(1).Dispatch(Arg.Is<OpenLogAction>(a =>
            a.LogName == Constants.LogNameLog2 && a.LogPathType == LogPathType.File));
    }

    private static (Effects effects, IDispatcher mockDispatcher) CreateEffects(
        bool continuouslyUpdate = false,
        ImmutableDictionary<string, EventLogData>? activeLogs = null,
        List<ResolvedEvent>? newEventBuffer = null,
        bool hasEventResolver = false)
    {
        var mockEventLogState = Substitute.For<IState<EventLogState>>();

        mockEventLogState.Value.Returns(new EventLogState
        {
            ContinuouslyUpdate = continuouslyUpdate,
            ActiveLogs = activeLogs ?? ImmutableDictionary<string, EventLogData>.Empty,
            NewEventBuffer = newEventBuffer ?? [],
            AppliedFilter = new EventFilter(null, [])
        });

        var mockFilterService = Substitute.For<IFilterService>();

        mockFilterService.FilterActiveLogs(Arg.Any<IEnumerable<EventLogData>>(), Arg.Any<EventFilter>())
            .Returns(new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>>());

        mockFilterService.GetFilteredEvents(Arg.Any<IEnumerable<ResolvedEvent>>(), Arg.Any<EventFilter>())
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
                .Returns(_ => EventUtils.CreateTestEvent(100));

            mockServiceProvider.GetService(typeof(IEventResolver)).Returns(mockEventResolver);
        }
        else
        {
            mockServiceProvider.GetService(typeof(IEventResolver)).Returns((IEventResolver?)null);
        }

        var mockDatabaseService = Substitute.For<IDatabaseService>();
        mockDatabaseService.InitialClassificationTask.Returns(Task.CompletedTask);

        var mockDispatcher = Substitute.For<IDispatcher>();

        var effects = new Effects(
            mockEventLogState,
            mockFilterService,
            mockLogger,
            mockLogWatcherService,
            mockResolverCache,
            Substitute.For<IEventXmlResolver>(),
            mockServiceScopeFactory,
            mockDatabaseService,
            Substitute.For<IBannerService>(),
            mockDispatcher);

        return (effects, mockDispatcher);
    }

    private static (Effects effects,
        IDispatcher mockDispatcher,
        IServiceProvider mockServiceProvider,
        IBannerService mockBannerService,
        IDatabaseService mockDatabaseService) CreateEffectsForOpenLogGuards(
            ImmutableDictionary<string, EventLogData> activeLogs)
    {
        var mockEventLogState = Substitute.For<IState<EventLogState>>();

        mockEventLogState.Value.Returns(new EventLogState
        {
            ActiveLogs = activeLogs,
            AppliedFilter = new EventFilter(null, [])
        });

        var mockServiceScopeFactory = Substitute.For<IServiceScopeFactory>();
        var mockServiceScope = Substitute.For<IServiceScope>();
        var mockServiceProvider = Substitute.For<IServiceProvider>();

        mockServiceScopeFactory.CreateScope().Returns(mockServiceScope);
        mockServiceScope.ServiceProvider.Returns(mockServiceProvider);

        var mockEventResolver = Substitute.For<IEventResolver>();

        mockEventResolver.ResolveEvent(Arg.Any<EventRecord>())
            .Returns(_ => EventUtils.CreateTestEvent(100));

        mockServiceProvider.GetService(typeof(IEventResolver)).Returns(mockEventResolver);

        var mockDatabaseService = Substitute.For<IDatabaseService>();
        mockDatabaseService.InitialClassificationTask.Returns(Task.CompletedTask);

        var mockBannerService = Substitute.For<IBannerService>();

        var mockDispatcher = Substitute.For<IDispatcher>();

        var effects = new Effects(
            mockEventLogState,
            Substitute.For<IFilterService>(),
            Substitute.For<ITraceLogger>(),
            Substitute.For<ILogWatcherService>(),
            Substitute.For<IEventResolverCache>(),
            Substitute.For<IEventXmlResolver>(),
            mockServiceScopeFactory,
            mockDatabaseService,
            mockBannerService,
            mockDispatcher);

        return (effects, mockDispatcher, mockServiceProvider, mockBannerService, mockDatabaseService);
    }

    private static (Effects effects,
        IDispatcher mockDispatcher,
        IFilterService mockFilterService) CreateEffectsWithMutableState(Func<EventLogState> stateProvider)
    {
        var mockEventLogState = Substitute.For<IState<EventLogState>>();
        mockEventLogState.Value.Returns(_ => stateProvider());

        var mockFilterService = Substitute.For<IFilterService>();

        mockFilterService.FilterActiveLogs(Arg.Any<IEnumerable<EventLogData>>(), Arg.Any<EventFilter>())
            .Returns(new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>>());

        mockFilterService.GetFilteredEvents(Arg.Any<IEnumerable<ResolvedEvent>>(), Arg.Any<EventFilter>())
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

        var effects = new Effects(
            mockEventLogState,
            mockFilterService,
            mockLogger,
            mockLogWatcherService,
            mockResolverCache,
            Substitute.For<IEventXmlResolver>(),
            mockServiceScopeFactory,
            mockDatabaseService,
            Substitute.For<IBannerService>(),
            mockDispatcher);

        return (effects, mockDispatcher, mockFilterService);
    }

    private static (Effects effects,
        IDispatcher mockDispatcher,
        ILogWatcherService mockLogWatcher,
        IEventResolverCache mockResolverCache,
        IFilterService mockFilterService) CreateEffectsWithServices(
            bool continuouslyUpdate = false,
            ImmutableDictionary<string, EventLogData>? activeLogs = null,
            List<ResolvedEvent>? newEventBuffer = null)
    {
        var mockEventLogState = Substitute.For<IState<EventLogState>>();

        mockEventLogState.Value.Returns(new EventLogState
        {
            ContinuouslyUpdate = continuouslyUpdate,
            ActiveLogs = activeLogs ?? ImmutableDictionary<string, EventLogData>.Empty,
            NewEventBuffer = newEventBuffer ?? [],
            AppliedFilter = new EventFilter(null, [])
        });

        var mockFilterService = Substitute.For<IFilterService>();

        mockFilterService.FilterActiveLogs(Arg.Any<IEnumerable<EventLogData>>(), Arg.Any<EventFilter>())
            .Returns(new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>>());

        mockFilterService.GetFilteredEvents(Arg.Any<IEnumerable<ResolvedEvent>>(), Arg.Any<EventFilter>())
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

        var effects = new Effects(
            mockEventLogState,
            mockFilterService,
            mockLogger,
            mockLogWatcherService,
            mockResolverCache,
            Substitute.For<IEventXmlResolver>(),
            mockServiceScopeFactory,
            mockDatabaseService,
            Substitute.For<IBannerService>(),
            mockDispatcher);

        return (effects, mockDispatcher, mockLogWatcherService, mockResolverCache, mockFilterService);
    }
}
