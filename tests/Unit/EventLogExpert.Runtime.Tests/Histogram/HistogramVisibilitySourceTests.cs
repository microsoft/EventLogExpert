// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Histogram;
using Fluxor;
using NSubstitute;

namespace EventLogExpert.Runtime.Tests.Histogram;

public sealed class HistogramVisibilitySourceTests
{
    [Fact]
    public void AThrowingSubscriber_IsIsolatedFromOtherSubscribers()
    {
        var harness = new Harness(visible: false);
        var reachedSecond = 0;
        harness.Source.Changed += () => throw new InvalidOperationException("subscriber blew up");
        harness.Source.Changed += () => reachedSecond++;

        harness.SetVisible(true);

        Assert.Equal(1, reachedSecond);
    }

    [Fact]
    public void Changed_DoesNotFire_WhenVisibilityUnchanged()
    {
        var harness = new Harness(visible: true);
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.SetState(new HistogramState { IsVisible = true, NextDimensionToken = 7 });

        Assert.Equal(0, raised);
        Assert.True(harness.Source.IsVisible);
    }

    [Fact]
    public void Changed_FiresWithLatestVisibility_WhenVisibilityFlips()
    {
        var harness = new Harness(visible: false);
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.SetVisible(true);

        Assert.Equal(1, raised);
        Assert.True(harness.Source.IsVisible);
    }

    [Fact]
    public void Construction_AdoptsAChangeThatLandsBetweenSeedAndSubscribe()
    {
        // then visible (the reconcile), with no StateChanged raised in between.
        var state = Substitute.For<IState<HistogramState>>();
        state.Value.Returns(new HistogramState { IsVisible = false }, new HistogramState { IsVisible = true });

        using var source = new HistogramVisibilitySource(state, Substitute.For<ITraceLogger>());

        Assert.True(source.IsVisible);
    }

    [Fact]
    public void Dispose_StopsRaising()
    {
        var harness = new Harness(visible: false);
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.Source.Dispose();
        harness.SetVisible(true);

        Assert.Equal(0, raised);
    }

    [Fact]
    public void IsVisible_ReflectsTheInitialState()
    {
        var harness = new Harness(visible: true);

        Assert.True(harness.Source.IsVisible);
    }

    private sealed class Harness
    {
        private readonly IState<HistogramState> _state = Substitute.For<IState<HistogramState>>();

        public Harness(bool visible)
        {
            State = new HistogramState { IsVisible = visible };
            _state.Value.Returns(_ => State);
            Source = new HistogramVisibilitySource(_state, Substitute.For<ITraceLogger>());
        }

        public HistogramVisibilitySource Source { get; }

        public HistogramState State { get; private set; }

        public void SetState(HistogramState state)
        {
            State = state;
            _state.StateChanged += Raise.Event<EventHandler>(_state, EventArgs.Empty);
        }

        public void SetVisible(bool visible) => SetState(new HistogramState { IsVisible = visible });
    }
}
