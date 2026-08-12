// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.EventLog;
using Fluxor;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Tests.EventLog;

public sealed class OpenLogsPresenceSourceTests
{
    [Fact]
    public void AThrowingSubscriber_IsIsolatedFromOtherSubscribers()
    {
        var harness = new Harness(openLogs: false);
        var reachedSecond = 0;
        harness.Source.Changed += () => throw new InvalidOperationException("subscriber blew up");
        harness.Source.Changed += () => reachedSecond++;

        harness.SetState(openLogs: true);

        Assert.Equal(1, reachedSecond);
    }

    [Fact]
    public void Changed_DoesNotFire_WhenPresenceUnchanged()
    {
        var harness = new Harness(openLogs: true);
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.SetState(WithOpenLogs(2));

        Assert.Equal(0, raised);
        Assert.True(harness.Source.HasOpenLogs);
    }

    [Fact]
    public void Changed_FiresWithLatestPresence_WhenPresenceFlips()
    {
        var harness = new Harness(openLogs: false);
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.SetState(openLogs: true);

        Assert.Equal(1, raised);
        Assert.True(harness.Source.HasOpenLogs);
    }

    [Fact]
    public void Construction_AdoptsAChangeThatLandsBetweenSeedAndSubscribe()
    {
        // seed) then an open-logs state (the reconcile), with no StateChanged raised in between.
        var state = Substitute.For<IState<EventLogState>>();
        state.Value.Returns(new EventLogState(), WithOpenLogs(1));

        using var source = new OpenLogsPresenceSource(state, Substitute.For<ITraceLogger>());

        Assert.True(source.HasOpenLogs);
    }

    [Fact]
    public void Dispose_StopsRaising()
    {
        var harness = new Harness(openLogs: false);
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.Source.Dispose();
        harness.SetState(openLogs: true);

        Assert.Equal(0, raised);
    }

    [Fact]
    public void HasOpenLogs_ReflectsTheInitialState()
    {
        var harness = new Harness(openLogs: true);

        Assert.True(harness.Source.HasOpenLogs);
    }

    private static EventLogState WithOpenLogs(int count)
    {
        var openLogs = ImmutableDictionary<string, OpenLogInfo>.Empty;

        for (var index = 0; index < count; index++)
        {
            openLogs = openLogs.Add($"Log{index}", new OpenLogInfo(EventLogId.Create(), LogPathType.Channel));
        }

        return new EventLogState { OpenLogs = openLogs };
    }

    private sealed class Harness
    {
        private readonly IState<EventLogState> _state = Substitute.For<IState<EventLogState>>();

        public Harness(bool openLogs)
        {
            State = openLogs ? WithOpenLogs(1) : new EventLogState();
            _state.Value.Returns(_ => State);
            Source = new OpenLogsPresenceSource(_state, Substitute.For<ITraceLogger>());
        }

        public OpenLogsPresenceSource Source { get; }

        public EventLogState State { get; private set; }

        public void SetState(bool openLogs) => SetState(openLogs ? WithOpenLogs(1) : new EventLogState());

        public void SetState(EventLogState state)
        {
            State = state;
            _state.StateChanged += Raise.Event<EventHandler>(_state, EventArgs.Empty);
        }
    }
}
