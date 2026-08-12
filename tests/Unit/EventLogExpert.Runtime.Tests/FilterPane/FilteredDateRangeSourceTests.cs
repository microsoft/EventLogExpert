// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.FilterPane;
using Fluxor;
using NSubstitute;

namespace EventLogExpert.Runtime.Tests.FilterPane;

public sealed class FilteredDateRangeSourceTests
{
    private static readonly DateFilter ARange = new() { After = DateTimeOffset.UnixEpoch.UtcDateTime };

    [Fact]
    public void AThrowingSubscriber_IsIsolatedFromOtherSubscribers()
    {
        var harness = new Harness(new FilterPaneState());
        var reachedSecond = 0;
        harness.Source.Changed += () => throw new InvalidOperationException("subscriber blew up");
        harness.Source.Changed += () => reachedSecond++;

        harness.SetState(new FilterPaneState { FilteredDateRange = ARange });

        Assert.Equal(1, reachedSecond);
    }

    [Fact]
    public void Changed_DoesNotFire_WhenRangeValueUnchanged()
    {
        var harness = new Harness(new FilterPaneState { FilteredDateRange = new DateFilter { After = ARange.After } });
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.SetState(new FilterPaneState { FilteredDateRange = new DateFilter { After = ARange.After } });

        Assert.Equal(0, raised);
    }

    [Fact]
    public void Changed_Fires_WhenRangeChanges()
    {
        var harness = new Harness(new FilterPaneState());
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.SetState(new FilterPaneState { FilteredDateRange = ARange });

        Assert.Equal(1, raised);
        Assert.Equal(ARange, harness.Source.Current);
    }

    [Fact]
    public void Changed_Fires_WhenRangeCleared()
    {
        var harness = new Harness(new FilterPaneState { FilteredDateRange = ARange });
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.SetState(new FilterPaneState());

        Assert.Equal(1, raised);
        Assert.Null(harness.Source.Current);
    }

    [Fact]
    public void Construction_AdoptsAChangeThatLandsBetweenSeedAndSubscribe()
    {
        var state = Substitute.For<IState<FilterPaneState>>();
        state.Value.Returns(new FilterPaneState(), new FilterPaneState { FilteredDateRange = ARange });

        using var source = new FilteredDateRangeSource(state, Substitute.For<ITraceLogger>());

        Assert.Equal(ARange, source.Current);
    }

    [Fact]
    public void Current_ReflectsTheInitialState()
    {
        var harness = new Harness(new FilterPaneState { FilteredDateRange = ARange });

        Assert.Equal(ARange, harness.Source.Current);
    }

    [Fact]
    public void Dispose_StopsRaising()
    {
        var harness = new Harness(new FilterPaneState());
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.Source.Dispose();
        harness.SetState(new FilterPaneState { FilteredDateRange = ARange });

        Assert.Equal(0, raised);
    }

    private sealed class Harness
    {
        private readonly IState<FilterPaneState> _state = Substitute.For<IState<FilterPaneState>>();

        public Harness(FilterPaneState initial)
        {
            State = initial;
            _state.Value.Returns(_ => State);
            Source = new FilteredDateRangeSource(_state, Substitute.For<ITraceLogger>());
        }

        public FilteredDateRangeSource Source { get; }

        public FilterPaneState State { get; private set; }

        public void SetState(FilterPaneState state)
        {
            State = state;
            _state.StateChanged += Raise.Event<EventHandler>(_state, EventArgs.Empty);
        }
    }
}
