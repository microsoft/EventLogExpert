// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventResolvers;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.EventLog;
using EventLogExpert.UI.Store.EventTable;
using EventLogExpert.UI.Store.StatusBar;
using EventLogExpert.UI.Tests.TestUtils;
using EventLogExpert.UI.Tests.TestUtils.Constants;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Tests.Store.EventLog;

public sealed class EventLogEffectsTests
{
    [Fact]
    public async Task HandleAddEvent_WhenBufferReachesMaxEvents_ShouldSetFullFlag()
    {
        // Arrange
        var existingBuffer = Enumerable.Range(0, EventLogState.MaxNewEvents - 1)
            .Select(i => EventUtils.CreateTestEvent(i, logName: Constants.LogNameTestLog))
            .ToList();

        var logData = new EventLogData(Constants.LogNameTestLog, PathType.LogName, []);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher) = CreateEffects(false, activeLogs, existingBuffer);

        var newEvent = EventUtils.CreateTestEvent(1000, logName: Constants.LogNameTestLog);
        var action = new EventLogAction.AddEvent(newEvent);

        // Act
        await effects.HandleAddEvent(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<EventLogAction.AddEventBuffered>(a =>
            a.IsFull == true && a.UpdatedBuffer.Count == EventLogState.MaxNewEvents));
    }

    [Fact]
    public async Task HandleAddEvent_WhenContinuouslyUpdateFalse_ShouldBufferEvent()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog, PathType.LogName, []);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher) = CreateEffects(
            false,
            activeLogs);

        var newEvent = EventUtils.CreateTestEvent(100, logName: Constants.LogNameTestLog);
        var action = new EventLogAction.AddEvent(newEvent);

        // Act
        await effects.HandleAddEvent(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<EventLogAction.AddEventBuffered>(a =>
            a.UpdatedBuffer.Count == 1 && a.UpdatedBuffer[0] == newEvent));
    }

    [Fact]
    public async Task HandleAddEvent_WhenContinuouslyUpdateTrue_ShouldDispatchSuccessAndUpdate()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog, PathType.LogName, []);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher) = CreateEffects(
            true,
            activeLogs);

        var newEvent = EventUtils.CreateTestEvent(100, logName: Constants.LogNameTestLog);
        var action = new EventLogAction.AddEvent(newEvent);

        // Act
        await effects.HandleAddEvent(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Any<EventLogAction.AddEventSuccess>());
        mockDispatcher.Received(1).Dispatch(Arg.Any<EventTableAction.UpdateDisplayedEvents>());
    }

    [Fact]
    public async Task HandleAddEvent_WhenLogNotActive_ShouldNotDispatchActions()
    {
        // Arrange
        var (effects, mockDispatcher) = CreateEffects();
        var newEvent = EventUtils.CreateTestEvent(100, logName: Constants.LogNameTestLog);
        var action = new EventLogAction.AddEvent(newEvent);

        // Act
        await effects.HandleAddEvent(action, mockDispatcher);

        // Assert - No dispatches should occur
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<object>());
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

        var effects = new EventLogEffects(
            mockEventLogState,
            Substitute.For<IFilterService>(),
            Substitute.For<ITraceLogger>(),
            Substitute.For<ILogWatcherService>(),
            Substitute.For<IEventResolverCache>(),
            mockXmlResolver,
            mockServiceScopeFactory);

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
        mockLogWatcher.Received(1).RemoveAll();
        mockResolverCache.Received(1).ClearAll();
        mockDispatcher.Received(1).Dispatch(Arg.Any<EventTableAction.CloseAll>());
        mockDispatcher.Received(1).Dispatch(Arg.Any<StatusBarAction.CloseAll>());
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

        var effects = new EventLogEffects(
            mockEventLogState,
            Substitute.For<IFilterService>(),
            Substitute.For<ITraceLogger>(),
            Substitute.For<ILogWatcherService>(),
            Substitute.For<IEventResolverCache>(),
            mockXmlResolver,
            mockServiceScopeFactory);

        var mockDispatcher = Substitute.For<IDispatcher>();
        var action = new EventLogAction.CloseLog(logId, Constants.LogNameTestLog);

        // Act
        await effects.HandleCloseLog(action, mockDispatcher);

        // Assert
        mockXmlResolver.Received(1).ClearLog(Constants.LogNameTestLog);
    }

    [Fact]
    public async Task HandleCloseLog_ShouldRemoveLogAndDispatchCloseAction()
    {
        // Arrange
        var logId = EventLogId.Create();
        var (effects, mockDispatcher, mockLogWatcher, _, _) = CreateEffectsWithServices();
        var action = new EventLogAction.CloseLog(logId, Constants.LogNameTestLog);

        // Act
        await effects.HandleCloseLog(action, mockDispatcher);

        // Assert
        mockLogWatcher.Received(1).RemoveLog(Constants.LogNameTestLog);

        mockDispatcher.Received(1).Dispatch(Arg.Is<EventTableAction.CloseLog>(a =>
            a.LogId == logId));
    }

    [Fact]
    public async Task HandleCloseLog_WhenLastLog_ShouldClearResolverCache()
    {
        // Arrange — state has no active logs (reducer already removed the last one)
        var logId = EventLogId.Create();
        var (effects, mockDispatcher, mockLogWatcher, mockResolverCache, _) = CreateEffectsWithServices();
        var action = new EventLogAction.CloseLog(logId, Constants.LogNameTestLog);

        // Act
        await effects.HandleCloseLog(action, mockDispatcher);

        // Assert
        mockResolverCache.Received(1).ClearAll();
    }

    [Fact]
    public async Task HandleCloseLog_WhenOtherLogsRemain_ShouldNotClearResolverCache()
    {
        // Arrange — state still has another active log
        var logData = new EventLogData(Constants.LogNameLog1, PathType.LogName, []);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty
            .Add(Constants.LogNameLog1, logData);

        var (effects, mockDispatcher, _, mockResolverCache, _) = CreateEffectsWithServices(activeLogs: activeLogs);
        var closingLogId = EventLogId.Create();
        var action = new EventLogAction.CloseLog(closingLogId, Constants.LogNameTestLog);

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

        var logData = new EventLogData(Constants.LogNameTestLog, PathType.LogName, []);
        var (effects, mockDispatcher, _, _, mockFilterService) = CreateEffectsWithServices();

        var action = new EventLogAction.LoadEvents(logData, events);

        // Act
        await effects.HandleLoadEvents(action, mockDispatcher);

        // Assert
        mockFilterService.Received(1).GetFilteredEvents(events, Arg.Any<EventFilter>());
        mockDispatcher.Received(1).Dispatch(Arg.Any<EventTableAction.UpdateTable>());
    }

    [Fact]
    public async Task HandleLoadNewEvents_ShouldProcessBufferAndDispatchActions()
    {
        // Arrange
        var bufferedEvents = new List<DisplayEventModel>
        {
            EventUtils.CreateTestEvent(100, logName: Constants.LogNameTestLog),
            EventUtils.CreateTestEvent(200, logName: Constants.LogNameTestLog)
        };

        var logData = new EventLogData(Constants.LogNameTestLog, PathType.LogName, []);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher) = CreateEffects(
            activeLogs: activeLogs,
            newEventBuffer: bufferedEvents);

        // Act
        await effects.HandleLoadNewEvents(mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Any<EventTableAction.UpdateDisplayedEvents>());
        mockDispatcher.Received(1).Dispatch(Arg.Any<EventLogAction.AddEventSuccess>());

        mockDispatcher.Received(1).Dispatch(Arg.Is<EventLogAction.AddEventBuffered>(a =>
            a.UpdatedBuffer.Count == 0 && a.IsFull == false));
    }

    [Fact]
    public async Task HandleOpenLog_WhenCancelled_ShouldDispatchCloseAndClearStatus()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameApplication, PathType.LogName, []);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameApplication, logData);

        var cts = new CancellationTokenSource();
        await cts.CancelAsync(); // Cancel immediately

        var (effects, mockDispatcher) = CreateEffects(
            activeLogs: activeLogs,
            hasEventResolver: true);

        var action = new EventLogAction.OpenLog(Constants.LogNameApplication, PathType.LogName, cts.Token);

        // Act
        await effects.HandleOpenLog(action, mockDispatcher);

        // Assert
        mockDispatcher.Received().Dispatch(Arg.Any<EventLogAction.CloseLog>());
        mockDispatcher.Received().Dispatch(Arg.Any<StatusBarAction.ClearStatus>());
    }

    [Fact]
    public async Task HandleOpenLog_WhenLogNotInActiveLogs_ShouldDispatchError()
    {
        // Arrange
        var (effects, mockDispatcher) = CreateEffects(hasEventResolver: true);
        var action = new EventLogAction.OpenLog(Constants.LogNameTestLog, PathType.LogName);

        // Act
        await effects.HandleOpenLog(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<StatusBarAction.SetResolverStatus>(a =>
            a.ResolverStatus.Contains("Error") && a.ResolverStatus.Contains(Constants.LogNameTestLog)));
    }

    [Fact]
    public async Task HandleOpenLog_WhenNoEventResolver_ShouldDispatchError()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameApplication, PathType.LogName, []);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameApplication, logData);

        var (effects, mockDispatcher) = CreateEffects(
            activeLogs: activeLogs,
            hasEventResolver: false);

        var action = new EventLogAction.OpenLog(Constants.LogNameApplication, PathType.LogName);

        // Act
        await effects.HandleOpenLog(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<StatusBarAction.SetResolverStatus>(a =>
            a.ResolverStatus.Contains("Error")));
    }

    [Fact]
    public async Task HandleSetContinuouslyUpdate_WhenFalse_ShouldNotProcessBuffer()
    {
        // Arrange
        var bufferedEvents = new List<DisplayEventModel>
        {
            EventUtils.CreateTestEvent(100, logName: Constants.LogNameTestLog)
        };

        var (effects, mockDispatcher) = CreateEffects(newEventBuffer: bufferedEvents);
        var action = new EventLogAction.SetContinuouslyUpdate(false);

        // Act
        await effects.HandleSetContinuouslyUpdate(action, mockDispatcher);

        // Assert
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<EventTableAction.UpdateDisplayedEvents>());
    }

    [Fact]
    public async Task HandleSetContinuouslyUpdate_WhenTrue_ShouldProcessBuffer()
    {
        // Arrange
        var bufferedEvents = new List<DisplayEventModel>
        {
            EventUtils.CreateTestEvent(100, logName: Constants.LogNameTestLog)
        };

        var logData = new EventLogData(Constants.LogNameTestLog, PathType.LogName, []);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher) = CreateEffects(
            activeLogs: activeLogs,
            newEventBuffer: bufferedEvents);

        var action = new EventLogAction.SetContinuouslyUpdate(true);

        // Act
        await effects.HandleSetContinuouslyUpdate(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Any<EventTableAction.UpdateDisplayedEvents>());
        mockDispatcher.Received(1).Dispatch(Arg.Any<EventLogAction.AddEventSuccess>());
    }

    [Fact]
    public async Task HandleSetFilters_ShouldFilterAndDispatchUpdate()
    {
        // Arrange
        var events = new List<DisplayEventModel>
        {
            EventUtils.CreateTestEvent(100, level: Constants.EventLevelError),
            EventUtils.CreateTestEvent(200, level: Constants.EventLevelInformation)
        };

        var logData = new EventLogData(Constants.LogNameTestLog, PathType.LogName, events);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher, _, _, mockFilterService) = CreateEffectsWithServices(activeLogs: activeLogs);

        var eventFilter = new EventFilter(null, []);
        var action = new EventLogAction.SetFilters(eventFilter);

        // Act
        await effects.HandleSetFilters(action, mockDispatcher);

        // Assert
        mockFilterService.Received(1).FilterActiveLogs(
            Arg.Any<IEnumerable<EventLogData>>(),
            eventFilter);

        mockDispatcher.Received(1).Dispatch(Arg.Any<EventTableAction.UpdateDisplayedEvents>());
    }

    [Fact]
    public async Task HandleSetFilters_WhenFilterDoesNotRequireXml_ShouldNotReloadLogs()
    {
        // Arrange — single active log + non-XML filter (Id-based). RequiresXml should be false,
        // so HandleSetFilters should fall through to UpdateDisplayedEvents and never close/open logs.
        var logData = new EventLogData(Constants.LogNameTestLog, PathType.LogName, []);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher, _, _, _) = CreateEffectsWithServices(activeLogs: activeLogs);

        var nonXmlFilter = new FilterModel
        {
            IsEnabled = true,
            Comparison = new FilterComparison { Value = "Id == 100" }
        };

        var eventFilter = new EventFilter(null, [nonXmlFilter]);
        var action = new EventLogAction.SetFilters(eventFilter);

        // Act
        await effects.HandleSetFilters(action, mockDispatcher);

        // Assert — UpdateDisplayedEvents path; no Close/Open dispatches.
        Assert.False(eventFilter.RequiresXml);
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<EventLogAction.CloseLog>());
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<EventLogAction.OpenLog>());
        mockDispatcher.Received(1).Dispatch(Arg.Any<EventTableAction.UpdateDisplayedEvents>());
    }

    [Fact]
    public async Task HandleSetFilters_WhenFilterRequiresXml_ShouldRestoreSelectionAfterReload()
    {
        // Arrange — active log with one previously-selected event (RecordId=42). After the
        // XML filter triggers a reload, HandleLoadEvents should consume the pending restore
        // entry and dispatch SelectEvents with the matching event from the new event set.
        var logData = new EventLogData(Constants.LogNameTestLog, PathType.LogName, []);
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

        mockFilterService.GetFilteredEvents(Arg.Any<IEnumerable<DisplayEventModel>>(), Arg.Any<EventFilter>())
            .Returns(callInfo => callInfo.Arg<IEnumerable<DisplayEventModel>>().ToList());

        var mockServiceScopeFactory = Substitute.For<IServiceScopeFactory>();
        var mockServiceScope = Substitute.For<IServiceScope>();
        var mockServiceProvider = Substitute.For<IServiceProvider>();

        mockServiceScopeFactory.CreateScope().Returns(mockServiceScope);
        mockServiceScope.ServiceProvider.Returns(mockServiceProvider);

        var effects = new EventLogEffects(
            mockEventLogState,
            mockFilterService,
            Substitute.For<ITraceLogger>(),
            Substitute.For<ILogWatcherService>(),
            Substitute.For<IEventResolverCache>(),
            Substitute.For<IEventXmlResolver>(),
            mockServiceScopeFactory);

        var mockDispatcher = Substitute.For<IDispatcher>();

        var xmlFilter = new FilterModel
        {
            IsEnabled = true,
            Comparison = new FilterComparison { Value = "Xml.Contains(\"foo\")" }
        };

        var eventFilter = new EventFilter(null, [xmlFilter]);

        // Act 1: Apply the XML filter — populates _pendingSelectionRestore for "TestLog".
        await effects.HandleSetFilters(new EventLogAction.SetFilters(eventFilter), mockDispatcher);

        // Act 2: Simulate the subsequent LoadEvents that the new OpenLog produces. The
        // reloaded events include the previously-selected RecordId=42 plus an unrelated one.
        var reloadedEvents = ImmutableArray.Create(
            EventUtils.CreateTestEvent(100, recordId: 42, logName: Constants.LogNameTestLog),
            EventUtils.CreateTestEvent(200, recordId: 99, logName: Constants.LogNameTestLog));

        await effects.HandleLoadEvents(new EventLogAction.LoadEvents(logData, reloadedEvents), mockDispatcher);

        // Assert — SetSelectedEvents dispatched with exactly the restored event (RecordId=42).
        mockDispatcher.Received(1).Dispatch(Arg.Is<EventLogAction.SetSelectedEvents>(a =>
            a.SelectedEvents.Count() == 1 && a.SelectedEvents.First().RecordId == 42));
    }

    [Fact]
    public async Task HandleSetFilters_WhenFilterRequiresXmlAndLogLacksXml_ShouldCloseAndReopenLog()
    {
        // Arrange — active log has not been loaded with XML, so it must be re-read.
        var logData = new EventLogData(Constants.LogNameTestLog, PathType.LogName, []);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher, _, _, _) = CreateEffectsWithServices(activeLogs: activeLogs);

        var xmlFilter = new FilterModel
        {
            IsEnabled = true,
            Comparison = new FilterComparison { Value = "Xml.Contains(\"foo\")" }
        };

        var eventFilter = new EventFilter(null, [xmlFilter]);
        var action = new EventLogAction.SetFilters(eventFilter);

        // Act
        await effects.HandleSetFilters(action, mockDispatcher);

        // Assert
        Assert.True(eventFilter.RequiresXml);

        mockDispatcher.Received(1).Dispatch(Arg.Is<EventLogAction.CloseLog>(a =>
            a.LogName == Constants.LogNameTestLog && a.LogId == logData.Id));

        mockDispatcher.Received(1).Dispatch(Arg.Is<EventLogAction.OpenLog>(a =>
            a.LogName == Constants.LogNameTestLog && a.PathType == PathType.LogName));

        // Reload path returns early — no UpdateDisplayedEvents until LoadEvents fires.
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<EventTableAction.UpdateDisplayedEvents>());
    }

    private static (EventLogEffects effects, IDispatcher mockDispatcher) CreateEffects(
        bool continuouslyUpdate = false,
        ImmutableDictionary<string, EventLogData>? activeLogs = null,
        List<DisplayEventModel>? newEventBuffer = null,
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
            .Returns(new Dictionary<EventLogId, IReadOnlyList<DisplayEventModel>>());

        mockFilterService.GetFilteredEvents(Arg.Any<IEnumerable<DisplayEventModel>>(), Arg.Any<EventFilter>())
            .Returns(callInfo => callInfo.Arg<IEnumerable<DisplayEventModel>>().ToList());

        var mockLogger = Substitute.For<ITraceLogger>();
        var mockLogWatcherService = Substitute.For<ILogWatcherService>();
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

        var effects = new EventLogEffects(
            mockEventLogState,
            mockFilterService,
            mockLogger,
            mockLogWatcherService,
            mockResolverCache,
            Substitute.For<IEventXmlResolver>(),
            mockServiceScopeFactory);

        var mockDispatcher = Substitute.For<IDispatcher>();

        return (effects, mockDispatcher);
    }

    private static (EventLogEffects effects,
        IDispatcher mockDispatcher,
        ILogWatcherService mockLogWatcher,
        IEventResolverCache mockResolverCache,
        IFilterService mockFilterService) CreateEffectsWithServices(
            bool continuouslyUpdate = false,
            ImmutableDictionary<string, EventLogData>? activeLogs = null,
            List<DisplayEventModel>? newEventBuffer = null)
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
            .Returns(new Dictionary<EventLogId, IReadOnlyList<DisplayEventModel>>());

        mockFilterService.GetFilteredEvents(Arg.Any<IEnumerable<DisplayEventModel>>(), Arg.Any<EventFilter>())
            .Returns(callInfo => callInfo.Arg<IEnumerable<DisplayEventModel>>().ToList());

        var mockLogger = Substitute.For<ITraceLogger>();
        var mockLogWatcherService = Substitute.For<ILogWatcherService>();
        var mockResolverCache = Substitute.For<IEventResolverCache>();

        var mockServiceScopeFactory = Substitute.For<IServiceScopeFactory>();
        var mockServiceScope = Substitute.For<IServiceScope>();
        var mockServiceProvider = Substitute.For<IServiceProvider>();

        mockServiceScopeFactory.CreateScope().Returns(mockServiceScope);
        mockServiceScope.ServiceProvider.Returns(mockServiceProvider);

        var effects = new EventLogEffects(
            mockEventLogState,
            mockFilterService,
            mockLogger,
            mockLogWatcherService,
            mockResolverCache,
            Substitute.For<IEventXmlResolver>(),
            mockServiceScopeFactory);

        var mockDispatcher = Substitute.For<IDispatcher>();

        return (effects, mockDispatcher, mockLogWatcherService, mockResolverCache, mockFilterService);
    }
}
