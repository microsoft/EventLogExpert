// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.EventLog;
using Fluxor;
using NSubstitute;

namespace EventLogExpert.Runtime.Tests.EventLog;

public sealed class FilterAppliedSourceTests
{
    [Fact]
    public void AThrowingSubscriber_IsIsolatedFromOtherSubscribers()
    {
        var harness = new Harness(filtering: false);
        var reachedSecond = 0;
        harness.Source.Changed += () => throw new InvalidOperationException("subscriber blew up");
        harness.Source.Changed += () => reachedSecond++;

        harness.SetState(filtering: true);

        Assert.Equal(1, reachedSecond);
    }

    [Fact]
    public void Changed_DoesNotFire_WhenFilteringUnchanged()
    {
        var harness = new Harness(filtering: true);
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.SetState(filtering: true);

        Assert.Equal(0, raised);
        Assert.True(harness.Source.IsFilteringEnabled);
    }

    [Fact]
    public void Changed_FiresWithLatestState_WhenFilteringFlips()
    {
        var harness = new Harness(filtering: false);
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.SetState(filtering: true);

        Assert.Equal(1, raised);
        Assert.True(harness.Source.IsFilteringEnabled);
    }

    [Fact]
    public void Construction_AdoptsAChangeThatLandsBetweenSeedAndSubscribe()
    {
        // (the seed) then a filtered state (the reconcile), with no StateChanged raised in between.
        var state = Substitute.For<IState<EventLogState>>();
        state.Value.Returns(new EventLogState(), Filtering());

        using var source = new FilterAppliedSource(state, Substitute.For<ITraceLogger>());

        Assert.True(source.IsFilteringEnabled);
    }

    [Fact]
    public void Dispose_StopsRaising()
    {
        var harness = new Harness(filtering: false);
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.Source.Dispose();
        harness.SetState(filtering: true);

        Assert.Equal(0, raised);
    }

    [Fact]
    public void IsFilteringEnabled_ReflectsTheInitialState()
    {
        var harness = new Harness(filtering: true);

        Assert.True(harness.Source.IsFilteringEnabled);
    }

    private static EventLogState Filtering() =>
        new() { AppliedFilter = new Filter(new DateFilter { IsEnabled = true }, []) };

    private sealed class Harness
    {
        private readonly IState<EventLogState> _state = Substitute.For<IState<EventLogState>>();

        public Harness(bool filtering)
        {
            State = filtering ? Filtering() : new EventLogState();
            _state.Value.Returns(_ => State);
            Source = new FilterAppliedSource(_state, Substitute.For<ITraceLogger>());
        }

        public FilterAppliedSource Source { get; }

        public EventLogState State { get; private set; }

        public void SetState(bool filtering) => SetState(filtering ? Filtering() : new EventLogState());

        public void SetState(EventLogState state)
        {
            State = state;
            _state.StateChanged += Raise.Event<EventHandler>(_state, EventArgs.Empty);
        }
    }
}
