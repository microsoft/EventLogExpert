// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.EventLog;
using Fluxor;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Tests.EventLog;

public sealed class EventSelectionSourceTests
{
    private static readonly EventLogId LogId = EventLogId.Create();
    private static readonly SelectionEntry AnEntry = Entry(0);

    [Fact]
    public void AThrowingSubscriber_IsIsolatedFromOtherSubscribers()
    {
        var harness = new Harness(ImmutableList<SelectionEntry>.Empty);
        var reachedSecond = 0;
        harness.Source.Changed += () => throw new InvalidOperationException("subscriber blew up");
        harness.Source.Changed += () => reachedSecond++;

        harness.SetSelection(ImmutableList.Create(AnEntry));

        Assert.Equal(1, reachedSecond);
    }

    [Fact]
    public void Changed_DoesNotFire_WhenSelectionReferenceUnchanged()
    {
        var selection = ImmutableList.Create(AnEntry);
        var harness = new Harness(selection);
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.SetState(new EventLogState { Selection = selection });

        Assert.Equal(0, raised);
        Assert.Same(selection, harness.Source.Current);
    }

    [Fact]
    public void Changed_Fires_WhenSelectionInstanceChanges()
    {
        var harness = new Harness(ImmutableList<SelectionEntry>.Empty);
        var raised = 0;
        harness.Source.Changed += () => raised++;

        var next = ImmutableList.Create(AnEntry);
        harness.SetSelection(next);

        Assert.Equal(1, raised);
        Assert.Same(next, harness.Source.Current);
    }

    [Fact]
    public void Construction_AdoptsAChangeThatLandsBetweenSeedAndSubscribe()
    {
        // populated list (the reconcile), with no StateChanged raised in between.
        var reconciled = ImmutableList.Create(AnEntry);
        var state = Substitute.For<IState<EventLogState>>();
        state.Value.Returns(new EventLogState(), new EventLogState { Selection = reconciled });

        using var source = new EventSelectionSource(state, Substitute.For<ITraceLogger>());

        Assert.Same(reconciled, source.Current);
    }

    [Fact]
    public void Current_ReflectsTheInitialState()
    {
        var selection = ImmutableList.Create(AnEntry);
        var harness = new Harness(selection);

        Assert.Same(selection, harness.Source.Current);
    }

    [Fact]
    public void Dispose_StopsRaising()
    {
        var harness = new Harness(ImmutableList<SelectionEntry>.Empty);
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.Source.Dispose();
        harness.SetSelection(ImmutableList.Create(AnEntry));

        Assert.Equal(0, raised);
    }

    private static SelectionEntry Entry(int index)
    {
        var handle = new EventLocator(LogId, 0, index);

        return new SelectionEntry(handle, handle, null);
    }

    private sealed class Harness
    {
        private readonly IState<EventLogState> _state = Substitute.For<IState<EventLogState>>();

        public Harness(ImmutableList<SelectionEntry> selection)
        {
            State = new EventLogState { Selection = selection };
            _state.Value.Returns(_ => State);
            Source = new EventSelectionSource(_state, Substitute.For<ITraceLogger>());
        }

        public EventSelectionSource Source { get; }

        public EventLogState State { get; private set; }

        public void SetSelection(ImmutableList<SelectionEntry> selection) =>
            SetState(new EventLogState { Selection = selection });

        public void SetState(EventLogState state)
        {
            State = state;
            _state.StateChanged += Raise.Event<EventHandler>(_state, EventArgs.Empty);
        }
    }
}
