// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.FilterLibrary;
using Fluxor;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Tests.FilterLibrary;

public sealed class LibraryEntriesSourceTests
{
    private static readonly LibraryEntry AnEntry =
        new LibraryEntryFilterSet { Name = "set", CreatedUtc = DateTimeOffset.UnixEpoch, Filters = [] };

    [Fact]
    public void AThrowingSubscriber_IsIsolatedFromOtherSubscribers()
    {
        var harness = new Harness(ImmutableList<LibraryEntry>.Empty);
        var reachedSecond = 0;
        harness.Source.Changed += () => throw new InvalidOperationException("subscriber blew up");
        harness.Source.Changed += () => reachedSecond++;

        harness.SetEntries(ImmutableList.Create(AnEntry));

        Assert.Equal(1, reachedSecond);
    }

    [Fact]
    public void Changed_DoesNotFire_WhenEntriesReferenceUnchanged()
    {
        var entries = ImmutableList.Create(AnEntry);
        var harness = new Harness(entries);
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.SetState(new FilterLibraryState { Entries = entries, IsLoaded = true });

        Assert.Equal(0, raised);
        Assert.Same(entries, harness.Source.Current);
    }

    [Fact]
    public void Changed_Fires_WhenEntriesInstanceChanges()
    {
        var harness = new Harness(ImmutableList<LibraryEntry>.Empty);
        var raised = 0;
        harness.Source.Changed += () => raised++;

        var next = ImmutableList.Create(AnEntry);
        harness.SetEntries(next);

        Assert.Equal(1, raised);
        Assert.Same(next, harness.Source.Current);
    }

    [Fact]
    public void Construction_AdoptsAChangeThatLandsBetweenSeedAndSubscribe()
    {
        // list (the reconcile), with no StateChanged raised in between.
        var reconciled = ImmutableList.Create(AnEntry);
        var state = Substitute.For<IState<FilterLibraryState>>();
        state.Value.Returns(new FilterLibraryState(), new FilterLibraryState { Entries = reconciled });

        using var source = new LibraryEntriesSource(state, Substitute.For<ITraceLogger>());

        Assert.Same(reconciled, source.Current);
    }

    [Fact]
    public void Current_ReflectsTheInitialState()
    {
        var entries = ImmutableList.Create(AnEntry);
        var harness = new Harness(entries);

        Assert.Same(entries, harness.Source.Current);
    }

    [Fact]
    public void Dispose_StopsRaising()
    {
        var harness = new Harness(ImmutableList<LibraryEntry>.Empty);
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.Source.Dispose();
        harness.SetEntries(ImmutableList.Create(AnEntry));

        Assert.Equal(0, raised);
    }

    private sealed class Harness
    {
        private readonly IState<FilterLibraryState> _state = Substitute.For<IState<FilterLibraryState>>();

        public Harness(ImmutableList<LibraryEntry> entries)
        {
            State = new FilterLibraryState { Entries = entries };
            _state.Value.Returns(_ => State);
            Source = new LibraryEntriesSource(_state, Substitute.For<ITraceLogger>());
        }

        public LibraryEntriesSource Source { get; }

        public FilterLibraryState State { get; private set; }

        public void SetEntries(ImmutableList<LibraryEntry> entries) =>
            SetState(new FilterLibraryState { Entries = entries });

        public void SetState(FilterLibraryState state)
        {
            State = state;
            _state.StateChanged += Raise.Event<EventHandler>(_state, EventArgs.Empty);
        }
    }
}
