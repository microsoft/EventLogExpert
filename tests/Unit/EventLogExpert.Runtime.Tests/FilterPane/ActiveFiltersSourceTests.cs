// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.FilterPane;
using Fluxor;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Tests.FilterPane;

public sealed class ActiveFiltersSourceTests
{
    private static readonly SavedFilter AFilter =
        new() { Color = HighlightColor.None, IsEnabled = true, ComparisonText = "Id == 1", Compiled = null! };

    [Fact]
    public void AThrowingSubscriber_IsIsolatedFromOtherSubscribers()
    {
        var harness = new Harness(ImmutableList<SavedFilter>.Empty);
        var reachedSecond = 0;
        harness.Source.Changed += () => throw new InvalidOperationException("subscriber blew up");
        harness.Source.Changed += () => reachedSecond++;

        harness.SetFilters(ImmutableList.Create(AFilter));

        Assert.Equal(1, reachedSecond);
    }

    [Fact]
    public void Changed_DoesNotFire_WhenFiltersReferenceUnchanged()
    {
        var filters = ImmutableList.Create(AFilter);
        var harness = new Harness(filters);
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.SetState(new FilterPaneState { Filters = filters });

        Assert.Equal(0, raised);
        Assert.Same(filters, harness.Source.Current);
    }

    [Fact]
    public void Changed_Fires_WhenFilterListInstanceChanges()
    {
        var harness = new Harness(ImmutableList<SavedFilter>.Empty);
        var raised = 0;
        harness.Source.Changed += () => raised++;

        var next = ImmutableList.Create(AFilter);
        harness.SetFilters(next);

        Assert.Equal(1, raised);
        Assert.Same(next, harness.Source.Current);
    }

    [Fact]
    public void Construction_AdoptsAChangeThatLandsBetweenSeedAndSubscribe()
    {
        // list (the reconcile), with no StateChanged raised in between.
        var reconciled = ImmutableList.Create(AFilter);
        var state = Substitute.For<IState<FilterPaneState>>();
        state.Value.Returns(new FilterPaneState(), new FilterPaneState { Filters = reconciled });

        using var source = new ActiveFiltersSource(state, Substitute.For<ITraceLogger>());

        Assert.Same(reconciled, source.Current);
    }

    [Fact]
    public void Current_ReflectsTheInitialState()
    {
        var filters = ImmutableList.Create(AFilter);
        var harness = new Harness(filters);

        Assert.Same(filters, harness.Source.Current);
    }

    [Fact]
    public void Dispose_StopsRaising()
    {
        var harness = new Harness(ImmutableList<SavedFilter>.Empty);
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.Source.Dispose();
        harness.SetFilters(ImmutableList.Create(AFilter));

        Assert.Equal(0, raised);
    }

    private sealed class Harness
    {
        private readonly IState<FilterPaneState> _state = Substitute.For<IState<FilterPaneState>>();

        public Harness(ImmutableList<SavedFilter> filters)
        {
            State = new FilterPaneState { Filters = filters };
            _state.Value.Returns(_ => State);
            Source = new ActiveFiltersSource(_state, Substitute.For<ITraceLogger>());
        }

        public ActiveFiltersSource Source { get; }

        public FilterPaneState State { get; private set; }

        public void SetFilters(ImmutableList<SavedFilter> filters) =>
            SetState(new FilterPaneState { Filters = filters });

        public void SetState(FilterPaneState state)
        {
            State = state;
            _state.StateChanged += Raise.Event<EventHandler>(_state, EventArgs.Empty);
        }
    }
}
