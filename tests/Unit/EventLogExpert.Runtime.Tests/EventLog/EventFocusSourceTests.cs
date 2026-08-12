// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.EventLog;
using Fluxor;
using NSubstitute;

namespace EventLogExpert.Runtime.Tests.EventLog;

public sealed class EventFocusSourceTests
{
    [Fact]
    public void AThrowingSubscriber_IsIsolatedFromOtherSubscribers()
    {
        var harness = new Harness(Focus(1));
        var reachedSecond = 0;
        harness.Source.Changed += () => throw new InvalidOperationException("subscriber blew up");
        harness.Source.Changed += () => reachedSecond++;

        harness.SetFocus(Focus(2));

        Assert.Equal(1, reachedSecond);
    }

    [Fact]
    public void Changed_DoesNotFire_WhenFocusUnchanged()
    {
        var focus = Focus(1);
        var harness = new Harness(focus);
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.SetState(new EventLogState { Focus = focus, ContinuouslyUpdate = true });

        Assert.Equal(0, raised);
    }

    [Fact]
    public void Changed_FiresWithLatestFocus_OnFocusChange()
    {
        var harness = new Harness(Focus(1));
        var raised = 0;
        harness.Source.Changed += () => raised++;

        var next = Focus(2);
        harness.SetFocus(next);

        Assert.Equal(1, raised);
        Assert.Equal(next, harness.Source.Current);
    }

    [Fact]
    public void Construction_AdoptsAChangeThatLandsBetweenSeedAndSubscribe()
    {
        // focused state (the reconcile), with no StateChanged raised in between.
        var seeded = Focus(7);
        var state = Substitute.For<IState<EventLogState>>();
        state.Value.Returns(new EventLogState(), new EventLogState { Focus = seeded });

        using var source = new EventFocusSource(state, Substitute.For<ITraceLogger>());

        Assert.Equal(seeded, source.Current);
    }

    [Fact]
    public void Current_ReflectsTheInitialFocus()
    {
        var focus = Focus(1);
        var harness = new Harness(focus);

        Assert.Equal(focus, harness.Source.Current);
    }

    [Fact]
    public void Dispose_StopsRaising()
    {
        var harness = new Harness(Focus(1));
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.Source.Dispose();
        harness.SetFocus(Focus(2));

        Assert.Equal(0, raised);
    }

    private static SelectionEntry Focus(int index)
    {
        var handle = new EventLocator(EventLogId.Create(), 0, index);

        return new SelectionEntry(handle, handle, null);
    }

    private sealed class Harness
    {
        private readonly IState<EventLogState> _state = Substitute.For<IState<EventLogState>>();

        public Harness(SelectionEntry? focus)
        {
            State = new EventLogState { Focus = focus };
            _state.Value.Returns(_ => State);
            Source = new EventFocusSource(_state, Substitute.For<ITraceLogger>());
        }

        public EventFocusSource Source { get; }

        public EventLogState State { get; private set; }

        public void SetFocus(SelectionEntry? focus) => SetState(new EventLogState { Focus = focus });

        public void SetState(EventLogState state)
        {
            State = state;
            _state.StateChanged += Raise.Event<EventHandler>(_state, EventArgs.Empty);
        }
    }
}
