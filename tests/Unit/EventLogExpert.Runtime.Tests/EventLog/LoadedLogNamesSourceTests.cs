// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.EventLog;
using Fluxor;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Tests.EventLog;

public sealed class LoadedLogNamesSourceTests
{
    [Fact]
    public void AThrowingSubscriber_IsIsolatedFromOtherSubscribers()
    {
        var harness = new Harness(ImmutableHashSet<string>.Empty);
        var reachedSecond = 0;
        harness.Source.Changed += () => throw new InvalidOperationException("subscriber blew up");
        harness.Source.Changed += () => reachedSecond++;

        harness.SetNames(ImmutableHashSet.Create("Application"));

        Assert.Equal(1, reachedSecond);
    }

    [Fact]
    public void Changed_DoesNotFire_WhenSetReferenceUnchanged()
    {
        var names = ImmutableHashSet.Create("Application");
        var harness = new Harness(names);
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.SetState(new EventLogState { LoadedLogNames = names });

        Assert.Equal(0, raised);
        Assert.Same(names, harness.Source.Current);
    }

    [Fact]
    public void Changed_Fires_WhenSetInstanceChanges()
    {
        var harness = new Harness(ImmutableHashSet<string>.Empty);
        var raised = 0;
        harness.Source.Changed += () => raised++;

        var next = ImmutableHashSet.Create("Application");
        harness.SetNames(next);

        Assert.Equal(1, raised);
        Assert.Same(next, harness.Source.Current);
    }

    [Fact]
    public void Construction_AdoptsAChangeThatLandsBetweenSeedAndSubscribe()
    {
        var reconciled = ImmutableHashSet.Create("Application");
        var state = Substitute.For<IState<EventLogState>>();
        state.Value.Returns(new EventLogState(), new EventLogState { LoadedLogNames = reconciled });

        using var source = new LoadedLogNamesSource(state, Substitute.For<ITraceLogger>());

        Assert.Same(reconciled, source.Current);
    }

    [Fact]
    public void Current_ReflectsTheInitialState()
    {
        var names = ImmutableHashSet.Create("Application");
        var harness = new Harness(names);

        Assert.Same(names, harness.Source.Current);
    }

    [Fact]
    public void Dispose_StopsRaising()
    {
        var harness = new Harness(ImmutableHashSet<string>.Empty);
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.Source.Dispose();
        harness.SetNames(ImmutableHashSet.Create("Application"));

        Assert.Equal(0, raised);
    }

    private sealed class Harness
    {
        private readonly IState<EventLogState> _state = Substitute.For<IState<EventLogState>>();

        public Harness(ImmutableHashSet<string> names)
        {
            State = new EventLogState { LoadedLogNames = names };
            _state.Value.Returns(_ => State);
            Source = new LoadedLogNamesSource(_state, Substitute.For<ITraceLogger>());
        }

        public LoadedLogNamesSource Source { get; }

        public EventLogState State { get; private set; }

        public void SetNames(ImmutableHashSet<string> names) => SetState(new EventLogState { LoadedLogNames = names });

        public void SetState(EventLogState state)
        {
            State = state;
            _state.StateChanged += Raise.Event<EventHandler>(_state, EventArgs.Empty);
        }
    }
}
