// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.TestUtils;
using EventLogExpert.Filtering.TestUtils.Constants;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.LogTable;
using Fluxor;
using NSubstitute;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class FilteredLogPresenceCoordinatorTests
{
    private const string LogName = "TestLog";

    [Fact]
    public void AppendCarryingTheFirstSurvivor_FlipsAKnownEmptyLogToNonEmpty()
    {
        var harness = Create();
        var logId = harness.OpenLog(rowCount: 2, computerName: FilterTestConstants.EventComputerServer02);
        harness.ApplyFilter(FilterOnServer01());
        harness.Coordinator.MarkFilterChanged();
        harness.SettlePresence();
        Assert.Equal(FilteredLogPresence.NoSurvivor, harness.VerdictFor(logId));
        harness.Dispatched.Clear();

        harness.AppendRows(logId, rowCount: 1, computerName: FilterTestConstants.EventComputerServer01);
        harness.Coordinator.MarkAppended([logId]);

        Assert.Equal(FilteredLogPresence.HasSurvivor, harness.VerdictFor(logId));
    }

    [Fact]
    public void AppendToAKnownNonEmptyLog_DoesNoFurtherWork()
    {
        var harness = Create();
        var logId = harness.OpenLog(rowCount: 2, computerName: FilterTestConstants.EventComputerServer01);
        harness.ApplyFilter(FilterOnServer01());
        harness.Coordinator.MarkFilterChanged();
        harness.SettlePresence();
        harness.Dispatched.Clear();

        harness.Coordinator.MarkAppended([logId]);

        Assert.Empty(harness.Dispatched.OfType<FilteredPresenceUpdatedAction>());
    }

    [Fact]
    public void ClosedLog_IsNotGivenAVerdict()
    {
        var harness = Create();
        var logId = harness.OpenLog(rowCount: 3);
        harness.CloseLogButLeaveStore(logId);

        harness.Coordinator.MarkFilterChanged();

        Assert.DoesNotContain(
            harness.Dispatched.OfType<FilteredPresenceUpdatedAction>().SelectMany(update => update.Verdicts),
            verdict => verdict.Key == logId);
    }

    [Fact]
    public void Dispose_ThenSignal_PublishesNothing()
    {
        var harness = Create();
        harness.OpenLog(rowCount: 1);
        harness.Coordinator.Dispose();
        harness.Dispatched.Clear();

        harness.Coordinator.MarkFilterChanged();

        Assert.Empty(harness.Dispatched);
    }

    [Fact]
    public void FilterChange_OpensAnEpochAndReturnsOpenLogsToPending()
    {
        var harness = Create();
        harness.OpenLog(rowCount: 1);

        harness.Coordinator.MarkFilterChanged();

        var invalidated = harness.Dispatched.OfType<FilteredPresenceInvalidatedAction>().Single();
        Assert.Equal(1, invalidated.FilterVersion);
        Assert.Single(invalidated.LogIds);
    }

    [Fact]
    public void FilterChange_PublishesAVerdictForEveryOpenLog()
    {
        var harness = Create();
        var withRows = harness.OpenLog(rowCount: 3);
        var empty = harness.OpenLog(rowCount: 0);

        harness.Coordinator.MarkFilterChanged();

        Assert.Equal(FilteredLogPresence.HasSurvivor, harness.VerdictFor(withRows));
        Assert.Equal(FilteredLogPresence.NoSurvivor, harness.VerdictFor(empty));
    }

    [Fact]
    public void FilterExcludingEveryRow_MarksTheLogKnownEmpty()
    {
        var harness = Create();
        var logId = harness.OpenLog(rowCount: 4, computerName: FilterTestConstants.EventComputerServer02);
        harness.ApplyFilter(FilterOnServer01());

        harness.Coordinator.MarkFilterChanged();

        Assert.Equal(FilteredLogPresence.NoSurvivor, harness.VerdictFor(logId));
    }

    [Fact]
    public void FilterMatchingOneRow_MarksTheLogNonEmpty()
    {
        var harness = Create();
        var logId = harness.OpenLog(rowCount: 4, computerName: FilterTestConstants.EventComputerServer01);
        harness.ApplyFilter(FilterOnServer01());

        harness.Coordinator.MarkFilterChanged();

        Assert.Equal(FilteredLogPresence.HasSurvivor, harness.VerdictFor(logId));
    }

    [Fact]
    public void RawEmptyLog_UnderAnyFilter_IsKnownEmptyWithoutScanning()
    {
        var harness = Create();
        harness.ApplyFilter(FilterOnServer01());
        var logId = harness.OpenLog(rowCount: 0);

        harness.Coordinator.MarkFilterChanged();

        Assert.Equal(FilteredLogPresence.NoSurvivor, harness.VerdictFor(logId));
    }

    [Fact]
    public void UnfilteredLog_WithRows_IsKnownNonEmptyWithoutScanning()
    {
        var harness = Create();
        var logId = harness.OpenLog(rowCount: 5);

        harness.Coordinator.MarkFilterChanged();

        Assert.Equal(FilteredLogPresence.HasSurvivor, harness.VerdictFor(logId));
    }

    [Fact]
    public void XmlRequiringFilter_AfterAPartialReload_RescansTheLogsThatDidNotReload()
    {
        var harness = Create();
        var logA = harness.OpenLog(rowCount: 3, loadedWithXml: true);
        var logB = harness.OpenLog(rowCount: 3, loadedWithXml: false);
        harness.ApplyFilter(FilterRequiringXml());

        harness.Coordinator.MarkFilterChanged();

        Assert.Empty(harness.Dispatched.OfType<FilteredPresenceUpdatedAction>());

        harness.CompleteXmlReload(logB);

        Assert.NotNull(harness.VerdictFor(logA));
        Assert.NotNull(harness.VerdictFor(logB));
    }

    [Fact]
    public void XmlRequiringFilter_WhenALogLosesItsXmlLoadMidScan_DoesNotDropTheOtherDirtyLogs()
    {
        var harness = Create();
        var logA = harness.OpenLog(rowCount: 3, loadedWithXml: true);
        var logB = harness.OpenLog(rowCount: 3, loadedWithXml: true);
        harness.ApplyFilter(FilterRequiringXml());
        harness.ClearXmlLoadOnNextDrain(logB);

        harness.Coordinator.MarkFilterChanged();

        Assert.Empty(harness.Dispatched.OfType<FilteredPresenceUpdatedAction>());

        harness.CompleteXmlReload(logB);

        Assert.NotNull(harness.VerdictFor(logA));
        Assert.NotNull(harness.VerdictFor(logB));
    }

    [Fact]
    public void XmlRequiringFilter_WhenTheDeferringLogIsClosed_RescansTheLogsThatRemain()
    {
        var harness = Create();
        var logA = harness.OpenLog(rowCount: 3, loadedWithXml: true);
        var logB = harness.OpenLog(rowCount: 3, loadedWithXml: false);
        harness.ApplyFilter(FilterRequiringXml());

        harness.Coordinator.MarkFilterChanged();
        Assert.Empty(harness.Dispatched.OfType<FilteredPresenceUpdatedAction>());

        harness.CloseLog(logB);

        Assert.NotNull(harness.VerdictFor(logA));
    }

    [Fact]
    public void XmlRequiringFilter_WithALogNotLoadedWithXml_PublishesNothing()
    {
        var harness = Create();
        harness.OpenLog(rowCount: 3, loadedWithXml: false);
        harness.ApplyFilter(FilterRequiringXml());

        harness.Coordinator.MarkFilterChanged();

        Assert.Empty(harness.Dispatched.OfType<FilteredPresenceUpdatedAction>());
    }

    private static Harness Create() => new();

    private static Filter FilterOnServer01() =>
        new(null, [FilterBuilder.CreateTestFilter(FilterTestConstants.FilterComputerNameEqualsServer01, isEnabled: true)]);

    private static Filter FilterRequiringXml() =>
        new(null, [FilterBuilder.CreateTestFilter(FilterTestConstants.FilterXmlContainsData, isEnabled: true)]);

    private sealed class Harness
    {
        private readonly EventLogConcurrencyState _concurrencyState = new();
        private readonly IState<EventLogState> _eventLogState = Substitute.For<IState<EventLogState>>();
        private readonly IState<FilteredLogPresenceState> _presenceState =
            Substitute.For<IState<FilteredLogPresenceState>>();
        private readonly IState<RawEventStoreState> _rawEventStore = Substitute.For<IState<RawEventStoreState>>();

        private EventLogState _eventLog = new();
        private FilteredLogPresenceState _presence = new();
        private RawEventStoreState _raw = new();

        public Harness()
        {
            var dispatcher = Substitute.For<IDispatcher>();
            dispatcher.When(target => target.Dispatch(Arg.Any<object>())).Do(call => Record(call.Arg<object>()!));

            _eventLogState.Value.Returns(_ => _eventLog);
            _rawEventStore.Value.Returns(_ => _raw);
            _presenceState.Value.Returns(_ => _presence);

            Coordinator = new FilteredLogPresenceCoordinator(
                dispatcher,
                _eventLogState,
                _rawEventStore,
                _presenceState,
                _concurrencyState,
                new XmlFilterMatchCache(),
                scanInline: true);
        }

        public FilteredLogPresenceCoordinator Coordinator { get; }

        public List<object> Dispatched { get; } = [];

        public void AppendRows(EventLogId logId, int rowCount, string computerName)
        {
            var existing = _raw.ByLog[logId];
            var appended = existing.Append(Rows(rowCount, computerName, startRecordId: existing.Count + 1));

            _raw = _raw with { ByLog = _raw.ByLog.SetItem(logId, appended) };
        }

        public void ApplyFilter(Filter filter) => _eventLog = _eventLog with { AppliedFilter = filter };

        public void ClearXmlLoadOnNextDrain(EventLogId logId) =>
            Coordinator.OnBatchDrainedForTest = () =>
            {
                _concurrencyState.ClearLoadedWithXml(logId);
                Coordinator.OnBatchDrainedForTest = null;
            };

        public void CloseLog(EventLogId logId)
        {
            CloseLogButLeaveStore(logId);
            Coordinator.Discard(logId);
        }

        public void CloseLogButLeaveStore(EventLogId logId)
        {
            var name = _eventLog.OpenLogs.First(pair => pair.Value.Id == logId).Key;

            _eventLog = _eventLog with { OpenLogs = _eventLog.OpenLogs.Remove(name) };
        }

        public void CompleteXmlReload(EventLogId logId)
        {
            _concurrencyState.MarkLoadedWithXml(logId);
            Coordinator.MarkRebuilt(logId);
        }

        public EventLogId OpenLog(int rowCount, string computerName = FilterTestConstants.EventComputerServer01, bool loadedWithXml = true)
        {
            var logId = EventLogId.Create();
            string name = $"{LogName}{_eventLog.OpenLogs.Count}";

            _eventLog = _eventLog with
            {
                OpenLogs = _eventLog.OpenLogs.SetItem(name, new OpenLogInfo(logId, LogPathType.Channel))
            };

            _raw = _raw with
            {
                ByLog = _raw.ByLog.SetItem(
                    logId,
                    EventColumnStore.Build(Rows(rowCount, computerName, startRecordId: 1), generation: 0, contentVersion: 1))
            };

            if (loadedWithXml) { _concurrencyState.MarkLoadedWithXml(logId); }

            return logId;
        }

        public void SettlePresence()
        {
            foreach (var invalidated in Dispatched.OfType<FilteredPresenceInvalidatedAction>())
            {
                _presence = _presence with { FilterVersion = invalidated.FilterVersion };
            }

            foreach (var update in Dispatched.OfType<FilteredPresenceUpdatedAction>())
            {
                if (update.FilterVersion != _presence.FilterVersion) { continue; }

                var byLog = _presence.ByLog;

                foreach (var (logId, presence) in update.Verdicts) { byLog = byLog.SetItem(logId, presence); }

                _presence = _presence with { ByLog = byLog };
            }
        }

        public FilteredLogPresence? VerdictFor(EventLogId logId)
        {
            foreach (var update in Enumerable.Reverse(Dispatched).OfType<FilteredPresenceUpdatedAction>())
            {
                foreach (var (candidate, presence) in update.Verdicts)
                {
                    if (candidate == logId) { return presence; }
                }
            }

            return null;
        }

        private static List<ResolvedEvent> Rows(int count, string computerName, int startRecordId)
        {
            var rows = new List<ResolvedEvent>(count);

            for (int index = 0; index < count; index++)
            {
                rows.Add(
                    new ResolvedEvent(LogName, LogPathType.Channel)
                    {
                        Id = 100 + index,
                        RecordId = startRecordId + index,
                        ComputerName = computerName
                    });
            }

            return rows;
        }

        private void Record(object action)
        {
            Dispatched.Add(action);

            if (action is FilteredPresenceInvalidatedAction invalidated)
            {
                var byLog = _presence.ByLog;

                foreach (var logId in invalidated.LogIds)
                {
                    byLog = byLog.SetItem(logId, FilteredLogPresence.Pending);
                }

                _presence = _presence with { ByLog = byLog, FilterVersion = invalidated.FilterVersion };
            }
        }
    }
}
