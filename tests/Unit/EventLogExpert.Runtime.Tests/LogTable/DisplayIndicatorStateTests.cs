// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.LogTable;
using static EventLogExpert.Runtime.Tests.LogTable.TestSupport.DisplayIndicatorTestFactory;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class DisplayIndicatorStateTests
{
    [Fact]
    public void AConditionBecomingWorthShowing_AsksForARender_BecauseNoPublicationSaysSo()
    {
        using var surface = new Surface();

        surface.Publish(DisplayIndicatorKind.EmptyPending);

        int before = surface.RenderRequests;

        surface.ElapseOnset();

        Assert.True(surface.RenderRequests > before, "a condition becoming worth showing must ask for a render");
    }

    [Fact]
    public void AConditionThatEndsFirst_ShowsNothingAtAll()
    {
        using var surface = new Surface();

        surface.Publish(DisplayIndicatorKind.EmptyPending);

        Assert.Equal(DisplayedIndicator.Nothing, surface.Paint(DisplayIndicatorKind.EmptyPending));

        surface.Publish(DisplayIndicatorKind.None);

        Assert.Equal(DisplayedIndicator.Nothing, surface.Paint(DisplayIndicatorKind.None));
    }

    [Fact]
    public void AConditionThatServesItsDelay_ShowsItsOwnSentence()
    {
        using var surface = new Surface();

        surface.Publish(DisplayIndicatorKind.EmptyPending);
        surface.ElapseOnset();

        var shown = surface.Paint(DisplayIndicatorKind.EmptyPending);

        Assert.Equal(DisplayIndicatorKind.EmptyPending, shown.Sentence);
        Assert.True(shown.Spinner);
    }

    [Fact]
    public void ARepaintingSurface_DoesNotKeepRestartingTheFloor()
    {
        using var surface = new Surface();

        surface.Publish(DisplayIndicatorKind.EmptyPending);
        surface.ElapseOnset();

        surface.Paint(DisplayIndicatorKind.EmptyPending);
        surface.Paint(DisplayIndicatorKind.EmptyPending);
        surface.Paint(DisplayIndicatorKind.EmptyPending);

        Assert.Single(surface.FloorDelaysRequested);
    }

    [Fact]
    public void ASpinnerAlreadyOnScreen_StaysForItsMinimumEvenAfterEverythingSettles()
    {
        using var surface = new Surface();

        surface.Publish(DisplayIndicatorKind.EmptyPending);
        surface.ElapseOnset();
        surface.Paint(DisplayIndicatorKind.EmptyPending);

        surface.Publish(DisplayIndicatorKind.None);

        var settledButFloored = surface.Paint(DisplayIndicatorKind.None);

        Assert.Equal(DisplayIndicatorKind.None, settledButFloored.Sentence);
        Assert.True(settledButFloored.Spinner, "the spinner owes its minimum visible time");

        surface.ElapseFloor();

        Assert.Equal(DisplayedIndicator.Nothing, surface.Paint(DisplayIndicatorKind.None));
    }

    [Fact]
    public void ASurfaceWhoseRowsAreStillArriving_KeepsTheSpinnerAfterTheStateSettles()
    {
        using var surface = new Surface();

        surface.Publish(DisplayIndicatorKind.EmptyPending);
        surface.ElapseOnset();
        surface.Paint(DisplayIndicatorKind.EmptyPending);

        surface.Publish(DisplayIndicatorKind.None);
        surface.ElapseFloor();

        Assert.True(
            surface.Paint(DisplayIndicatorKind.None, surfaceStillCatchingUp: true).Spinner,
            "the rows have not landed yet");

        Assert.Equal(
            DisplayedIndicator.Nothing,
            surface.Paint(DisplayIndicatorKind.None, surfaceStillCatchingUp: false));
    }

    [Fact]
    public void ASwapBetweenTwoConditions_DropsTheOldSentenceImmediatelyAndKeepsAGenericSpinner()
    {
        using var surface = new Surface();

        surface.Publish(DisplayIndicatorKind.ReorderPending);
        surface.ElapseOnset();

        Assert.Equal(DisplayIndicatorKind.ReorderPending, surface.Paint(DisplayIndicatorKind.ReorderPending).Sentence);

        surface.Publish(DisplayIndicatorKind.Fault);

        var duringTheSwap = surface.Paint(DisplayIndicatorKind.Fault);

        Assert.Equal(DisplayIndicatorKind.None, duringTheSwap.Sentence);
        Assert.True(duringTheSwap.Spinner, "the swap must not blink");

        surface.ElapseOnset();

        Assert.Equal(DisplayIndicatorKind.Fault, surface.Paint(DisplayIndicatorKind.Fault).Sentence);
    }

    [Fact]
    public void AnExpiringFloor_AsksForARender_BecauseNothingElseWill()
    {
        using var surface = new Surface();

        surface.Publish(DisplayIndicatorKind.EmptyPending);
        surface.ElapseOnset();
        surface.Paint(DisplayIndicatorKind.EmptyPending);
        surface.Publish(DisplayIndicatorKind.None);
        surface.Paint(DisplayIndicatorKind.None);

        int before = surface.RenderRequests;

        surface.ElapseFloor();

        Assert.True(surface.RenderRequests > before, "the floor's expiry must ask for the paint that ends it");
    }

    [Fact]
    public void NothingIsRetainedIfNothingWasEverShown()
    {
        using var surface = new Surface();

        surface.Publish(DisplayIndicatorKind.EmptyPending);
        surface.Paint(DisplayIndicatorKind.EmptyPending);

        surface.Publish(DisplayIndicatorKind.Fault);

        Assert.Equal(DisplayedIndicator.Nothing, surface.Paint(DisplayIndicatorKind.Fault));
    }

    [Fact]
    public void TheFloorStartsAtThePaint_NotWhenTheConditionBecameWorthShowing()
    {
        using var surface = new Surface();

        surface.Publish(DisplayIndicatorKind.EmptyPending);
        surface.ElapseOnset();

        Assert.Empty(surface.FloorDelaysRequested);

        surface.Paint(DisplayIndicatorKind.EmptyPending);

        Assert.Equal(TimeSpan.FromMilliseconds(300), Assert.Single(surface.FloorDelaysRequested));
    }

    [Fact]
    public void TheSentenceIsNeverFloored_OnlyTheSpinnerIs()
    {
        using var surface = new Surface();

        surface.Publish(DisplayIndicatorKind.ReorderPending);
        surface.ElapseOnset();
        surface.Paint(DisplayIndicatorKind.ReorderPending);

        surface.Publish(DisplayIndicatorKind.None);

        Assert.Equal(DisplayIndicatorKind.None, surface.Paint(DisplayIndicatorKind.None).Sentence);
    }

    private sealed class ControllableDelay
    {
        private readonly List<Pending> _pending = [];
        private readonly Lock _sync = new();

        public List<TimeSpan> Requested { get; } = [];

        public Task Delay(TimeSpan duration, CancellationToken token)
        {
            // Default (synchronous) continuations: completing this in Elapse() runs the awaiting onset/floor
            // logic inline on the caller, so a test can assert the resulting state directly instead of polling for
            // a thread-pool continuation (whose scheduling slipped past the old real-clock deadline under CI load).
            var completion = new TaskCompletionSource();

            lock (_sync)
            {
                Requested.Add(duration);
                _pending.Add(new Pending(completion, token));
            }

            return completion.Task;
        }

        public void Elapse()
        {
            RequireInlineContinuations();

            Pending[] outstanding;

            lock (_sync)
            {
                outstanding = [.. _pending];

                _pending.Clear();
            }

            foreach (var pending in outstanding)
            {
                if (pending.Token.IsCancellationRequested)
                {
                    pending.Completion.TrySetCanceled(pending.Token);
                }
                else
                {
                    pending.Completion.TrySetResult();
                }
            }
        }

        // Completing a delay's TaskCompletionSource (see Delay) only runs the awaiting onset/floor continuation
        // inline when the ambient host leaves no SynchronizationContext and the default TaskScheduler in place -
        // which the xUnit v3 test host does. Assert it here so any future host change fails loudly at the point
        // of reliance instead of silently reintroducing the timing flakiness this design replaced.
        private static void RequireInlineContinuations()
        {
            Assert.Null(SynchronizationContext.Current);
            Assert.Same(TaskScheduler.Default, TaskScheduler.Current);
        }

        private sealed record Pending(TaskCompletionSource Completion, CancellationToken Token);
    }

    private sealed class FakeSource(OrderedViewPresentation initial) : IOrderedViewSource
    {
        public event Action<OrderedViewPresentation>? Updated;

        public OrderedViewPresentation Current { get; private set; } = initial;

        public void Publish(OrderedViewPresentation presentation)
        {
            Current = presentation;

            Updated?.Invoke(presentation);
        }
    }

    private sealed class Surface : IDisposable
    {
        private readonly ControllableDelay _floorDelay = new();
        private readonly DisplayIndicatorGate _gate;
        private readonly ControllableDelay _onsetDelay = new();
        private readonly FakeSource _source;
        private readonly DisplayIndicatorState _state;

        private int _renderRequests;

        public Surface()
        {
            _source = new FakeSource(PresentationFor(DisplayIndicatorKind.None, revision: 0));
            _gate = new DisplayIndicatorGate(_source, _onsetDelay.Delay);
            _state = new DisplayIndicatorState(
                _gate,
                () => Interlocked.Increment(ref _renderRequests),
                _floorDelay.Delay);
        }

        public IReadOnlyList<TimeSpan> FloorDelaysRequested => _floorDelay.Requested;

        public int RenderRequests => Volatile.Read(ref _renderRequests);

        private long Revision { get; set; }

        public void Dispose()
        {
            _state.Dispose();
            _gate.Dispose();
        }

        public void ElapseFloor()
        {
            int before = RenderRequests;

            _floorDelay.Elapse();

            Assert.True(RenderRequests > before, "the floor's expiry must ask for the paint that ends it");
        }

        public void ElapseOnset()
        {
            bool announced = false;

            void OnElapsed() => announced = true;

            _gate.OnsetElapsed += OnElapsed;

            try
            {
                _onsetDelay.Elapse();

                Assert.True(announced, "the onset must fire once its delay elapses");
            }
            finally { _gate.OnsetElapsed -= OnElapsed; }
        }

        public DisplayedIndicator Paint(DisplayIndicatorKind kind, bool surfaceStillCatchingUp = false)
        {
            var shown = _state.Resolve(kind, Revision, surfaceStillCatchingUp);

            _state.RecordPaint(shown);

            return shown;
        }

        public void Publish(DisplayIndicatorKind kind)
        {
            Revision++;

            _source.Publish(PresentationFor(kind, Revision));
        }
    }
}
