// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.FilterLibrary;
using Fluxor;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Tests.FilterLibrary;

public sealed class LibraryLoadStatusSourceTests
{
    private static readonly LibraryEntry AnEntry =
        new LibraryEntryFilterSet { Name = "set", CreatedUtc = DateTimeOffset.UnixEpoch, Filters = [] };

    [Fact]
    public void AThrowingSubscriber_IsIsolatedFromOtherSubscribers()
    {
        var harness = new Harness(new FilterLibraryState());
        var reachedSecond = 0;
        harness.Source.Changed += () => throw new InvalidOperationException("subscriber blew up");
        harness.Source.Changed += () => reachedSecond++;

        harness.SetState(new FilterLibraryState { IsLoaded = true });

        Assert.Equal(1, reachedSecond);
    }

    [Fact]
    public void Changed_DoesNotFire_WhenStatusUnchanged()
    {
        var harness = new Harness(new FilterLibraryState { IsLoaded = true });
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.SetState(new FilterLibraryState { IsLoaded = true, Entries = ImmutableList.Create(AnEntry) });

        Assert.Equal(0, raised);
    }

    [Fact]
    public void Changed_Fires_WhenIsLoadedFlips()
    {
        var harness = new Harness(new FilterLibraryState());
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.SetState(new FilterLibraryState { IsLoaded = true });

        Assert.Equal(1, raised);
        Assert.Equal(new LibraryLoadStatus(true, false), harness.Source.Current);
    }

    [Fact]
    public void Changed_Fires_WhenLoadErrorFlips()
    {
        var harness = new Harness(new FilterLibraryState { IsLoaded = true });
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.SetState(new FilterLibraryState { IsLoaded = true, LoadError = true });

        Assert.Equal(1, raised);
        Assert.Equal(new LibraryLoadStatus(true, true), harness.Source.Current);
    }

    [Fact]
    public void Construction_AdoptsAChangeThatLandsBetweenSeedAndSubscribe()
    {
        // loaded status (the reconcile), with no StateChanged raised in between.
        var state = Substitute.For<IState<FilterLibraryState>>();
        state.Value.Returns(new FilterLibraryState(), new FilterLibraryState { IsLoaded = true });

        using var source = new LibraryLoadStatusSource(state, Substitute.For<ITraceLogger>());

        Assert.Equal(new LibraryLoadStatus(true, false), source.Current);
    }

    [Fact]
    public void Current_ReflectsTheInitialState()
    {
        var harness = new Harness(new FilterLibraryState { IsLoaded = true });

        Assert.Equal(new LibraryLoadStatus(true, false), harness.Source.Current);
    }

    [Fact]
    public void Dispose_StopsRaising()
    {
        var harness = new Harness(new FilterLibraryState());
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.Source.Dispose();
        harness.SetState(new FilterLibraryState { IsLoaded = true });

        Assert.Equal(0, raised);
    }

    private sealed class Harness
    {
        private readonly IState<FilterLibraryState> _state = Substitute.For<IState<FilterLibraryState>>();

        public Harness(FilterLibraryState initial)
        {
            State = initial;
            _state.Value.Returns(_ => State);
            Source = new LibraryLoadStatusSource(_state, Substitute.For<ITraceLogger>());
        }

        public LibraryLoadStatusSource Source { get; }

        public FilterLibraryState State { get; private set; }

        public void SetState(FilterLibraryState state)
        {
            State = state;
            _state.StateChanged += Raise.Event<EventHandler>(_state, EventArgs.Empty);
        }
    }
}
