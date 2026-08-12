// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.LogTable;
using Fluxor;
using NSubstitute;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class ActiveEventLogSourceTests
{
    [Fact]
    public void AThrowingSubscriber_IsIsolatedFromOtherSubscribers()
    {
        var harness = new Harness(EventLogId.Create());
        var reachedSecond = 0;
        harness.Source.Changed += () => throw new InvalidOperationException("subscriber blew up");
        harness.Source.Changed += () => reachedSecond++;

        harness.SetActive(EventLogId.Create());

        Assert.Equal(1, reachedSecond);
    }

    [Fact]
    public void Changed_DoesNotFire_WhenActiveTabUnchanged()
    {
        var active = EventLogId.Create();
        var harness = new Harness(active);
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.SetState(new LogTableState { ActiveEventLogId = active, GroupBy = ColumnName.Source });

        Assert.Equal(0, raised);
    }

    [Fact]
    public void Changed_FiresWithLatestActiveTab_OnChange()
    {
        var harness = new Harness(EventLogId.Create());
        var raised = 0;
        harness.Source.Changed += () => raised++;

        var next = EventLogId.Create();
        harness.SetActive(next);

        Assert.Equal(1, raised);
        Assert.Equal(next, harness.Source.Current);
    }

    [Fact]
    public void Construction_AdoptsAChangeThatLandsBetweenSeedAndSubscribe()
    {
        // an active-tab state (the reconcile), with no StateChanged raised in between.
        var seeded = EventLogId.Create();
        var state = Substitute.For<IState<LogTableState>>();
        state.Value.Returns(new LogTableState(), new LogTableState { ActiveEventLogId = seeded });

        using var source = new ActiveEventLogSource(state, Substitute.For<ITraceLogger>());

        Assert.Equal(seeded, source.Current);
    }

    [Fact]
    public void Current_ReflectsTheInitialActiveTab()
    {
        var active = EventLogId.Create();
        var harness = new Harness(active);

        Assert.Equal(active, harness.Source.Current);
    }

    [Fact]
    public void Dispose_StopsRaising()
    {
        var harness = new Harness(EventLogId.Create());
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.Source.Dispose();
        harness.SetActive(EventLogId.Create());

        Assert.Equal(0, raised);
    }

    private sealed class Harness
    {
        private readonly IState<LogTableState> _state = Substitute.For<IState<LogTableState>>();

        public Harness(EventLogId? active)
        {
            State = new LogTableState { ActiveEventLogId = active };
            _state.Value.Returns(_ => State);
            Source = new ActiveEventLogSource(_state, Substitute.For<ITraceLogger>());
        }

        public ActiveEventLogSource Source { get; }

        public LogTableState State { get; private set; }

        public void SetActive(EventLogId? active) => SetState(new LogTableState { ActiveEventLogId = active });

        public void SetState(LogTableState state)
        {
            State = state;
            _state.StateChanged += Raise.Event<EventHandler>(_state, EventArgs.Empty);
        }
    }
}
