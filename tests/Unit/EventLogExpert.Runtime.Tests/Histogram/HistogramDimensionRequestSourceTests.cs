// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Histogram;
using Fluxor;
using NSubstitute;

namespace EventLogExpert.Runtime.Tests.Histogram;

public sealed class HistogramDimensionRequestSourceTests
{
    [Fact]
    public void AThrowingSubscriber_IsIsolatedFromOtherSubscribers()
    {
        var harness = new Harness(new HistogramDimensionRequest(HistogramDimension.Severity, 1));
        var reachedSecond = 0;
        harness.Source.Changed += () => throw new InvalidOperationException("subscriber blew up");
        harness.Source.Changed += () => reachedSecond++;

        harness.SetRequest(new HistogramDimensionRequest(HistogramDimension.EventId, 2));

        Assert.Equal(1, reachedSecond);
    }

    [Fact]
    public void Changed_DoesNotFire_WhenDimensionRequestUnchanged()
    {
        var harness = new Harness(new HistogramDimensionRequest(HistogramDimension.Severity, 1));
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.SetState(new HistogramState
        {
            DimensionRequest = new HistogramDimensionRequest(HistogramDimension.Severity, 1),
            IsVisible = true
        });

        Assert.Equal(0, raised);
        Assert.Equal(new HistogramDimensionRequest(HistogramDimension.Severity, 1), harness.Source.Current);
    }

    [Fact]
    public void Changed_FiresWithLatestRequest_WhenRequestChanges()
    {
        var harness = new Harness(new HistogramDimensionRequest(HistogramDimension.Severity, 1));
        var raised = 0;
        harness.Source.Changed += () => raised++;

        var next = new HistogramDimensionRequest(HistogramDimension.EventId, 2);
        harness.SetRequest(next);

        Assert.Equal(1, raised);
        Assert.Equal(next, harness.Source.Current);
    }

    [Fact]
    public void Construction_AdoptsAChangeThatLandsBetweenSeedAndSubscribe()
    {
        // (the reconcile), with no StateChanged raised in between.
        var reconciled = new HistogramDimensionRequest(HistogramDimension.EventId, 5);
        var state = Substitute.For<IState<HistogramState>>();
        state.Value.Returns(new HistogramState(), new HistogramState { DimensionRequest = reconciled });

        using var source = new HistogramDimensionRequestSource(state, Substitute.For<ITraceLogger>());

        Assert.Equal(reconciled, source.Current);
    }

    [Fact]
    public void Current_ReflectsTheInitialState()
    {
        var request = new HistogramDimensionRequest(HistogramDimension.Log, 3);
        var harness = new Harness(request);

        Assert.Equal(request, harness.Source.Current);
    }

    [Fact]
    public void Dispose_StopsRaising()
    {
        var harness = new Harness(new HistogramDimensionRequest(HistogramDimension.Severity, 1));
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.Source.Dispose();
        harness.SetRequest(new HistogramDimensionRequest(HistogramDimension.EventId, 2));

        Assert.Equal(0, raised);
    }

    private sealed class Harness
    {
        private readonly IState<HistogramState> _state = Substitute.For<IState<HistogramState>>();

        public Harness(HistogramDimensionRequest? request)
        {
            State = new HistogramState { DimensionRequest = request };
            _state.Value.Returns(_ => State);
            Source = new HistogramDimensionRequestSource(_state, Substitute.For<ITraceLogger>());
        }

        public HistogramDimensionRequestSource Source { get; }

        public HistogramState State { get; private set; }

        public void SetRequest(HistogramDimensionRequest? request) =>
            SetState(new HistogramState { DimensionRequest = request });

        public void SetState(HistogramState state)
        {
            State = state;
            _state.StateChanged += Raise.Event<EventHandler>(_state, EventArgs.Empty);
        }
    }
}
