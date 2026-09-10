// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.LogTable;
using static EventLogExpert.Runtime.Tests.LogTable.TestSupport.DisplayIndicatorTestFactory;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class DisplayIndicatorGateTests
{
    [Fact]
    public void AFaultLandingMidSort_ReArms_BecauseItIsADifferentKindEvenThoughSomethingWasAlreadyOwed()
    {
        using var world = new Gate();

        world.Publish(DisplayIndicatorKind.ReorderPending);
        world.ElapseUntilFired();

        Assert.True(world.IsFired(DisplayIndicatorKind.ReorderPending));

        world.Publish(DisplayIndicatorKind.Fault);

        Assert.False(world.IsFired(DisplayIndicatorKind.Fault), "the fault must serve its own delay");

        world.ElapseUntilFired();

        Assert.True(world.IsFired(DisplayIndicatorKind.Fault));
    }

    [Fact]
    public void AGateBuiltOverAnAlreadyPendingDisplay_ArmsFromWhatIsAlreadyOnScreen()
    {
        var source = new FakeSource(PresentationFor(DisplayIndicatorKind.EmptyPending, revision: 7));
        var delay = new ControllableDelay();

        using var gate = new DisplayIndicatorGate(source, delay.Delay);

        Assert.Single(delay.Requested);

        delay.Elapse();
        Assert.True(gate.IsFiredFor(DisplayIndicatorKind.EmptyPending, 7), "the onset must fire once its delay elapses");
    }

    [Fact]
    public void AKindThatEndsBeforeItsDelay_NeverBecomesWorthShowing()
    {
        using var world = new Gate();

        world.Publish(DisplayIndicatorKind.EmptyPending);
        world.Publish(DisplayIndicatorKind.None);
        world.ElapseExpectingNothing();

        Assert.False(world.IsFired(DisplayIndicatorKind.EmptyPending));
    }

    [Fact]
    public void AKindThatRecurs_ServesItsOwnDelayEachTime_RatherThanResumingTheFirstOne()
    {
        using var world = new Gate();

        world.Publish(DisplayIndicatorKind.EmptyPending);
        world.ElapseUntilFired();

        Assert.True(world.IsFired(DisplayIndicatorKind.EmptyPending));

        world.Publish(DisplayIndicatorKind.ReorderPending);
        world.Publish(DisplayIndicatorKind.EmptyPending);

        Assert.False(
            world.IsFired(DisplayIndicatorKind.EmptyPending),
            "the second empty indicator must serve its own delay");
    }

    [Fact]
    public void ASupersededIndicator_CannotFire_EvenIfItsDelayCompletesAfterBeingReplaced()
    {
        using var world = new Gate();

        long supersededRevision = world.Publish(DisplayIndicatorKind.EmptyPending);

        world.Publish(DisplayIndicatorKind.ReorderPending);

        world.ElapseIgnoringCancellationUntilFired(expectedAnnouncements: 1);

        Assert.False(
            world.IsFired(DisplayIndicatorKind.EmptyPending, supersededRevision),
            "the superseded indicator must not fire even though its own delay completed");

        Assert.True(world.IsFired(DisplayIndicatorKind.ReorderPending), "the live indicator must still fire");
    }

    [Fact]
    public void ASurfaceStillPaintingAnEarlierFrame_IsAnsweredAboutItsOwnFrame_NotTheOneItHasNotSeen()
    {
        using var world = new Gate();

        long laggingRevision = world.Publish(DisplayIndicatorKind.Fault);

        world.ElapseUntilFired();

        Assert.True(world.IsFired(DisplayIndicatorKind.Fault, laggingRevision));

        world.Publish(DisplayIndicatorKind.None);
        world.Publish(DisplayIndicatorKind.Fault);

        Assert.True(
            world.IsFired(DisplayIndicatorKind.Fault, laggingRevision),
            "a surface on the older frame must still see the indicator that was current for it");

        Assert.False(
            world.IsFired(DisplayIndicatorKind.Fault, world.Revision),
            "a surface on the newest frame must see the re-armed indicator, which has not fired");
    }

    [Fact]
    public void AnElapsedOnset_IsAnnounced_BecauseNoPublicationDescribesIt()
    {
        using var world = new Gate();

        world.Publish(DisplayIndicatorKind.EmptyPending);

        Assert.Equal(0, world.Announcements);

        world.ElapseUntilFired();

        Assert.Equal(1, world.Announcements);
    }

    [Fact]
    public void OrdinaryRepublication_DoesNotRestartTheDelay()
    {
        using var world = new Gate();

        world.Publish(DisplayIndicatorKind.EmptyPending);
        world.Publish(DisplayIndicatorKind.EmptyPending);
        world.Publish(DisplayIndicatorKind.EmptyPending);

        Assert.Single(world.RequestedDelays);

        world.ElapseUntilFired();

        Assert.True(world.IsFired(DisplayIndicatorKind.EmptyPending));
    }

    [Fact]
    public void TheDelayAsked_IsTheOneTheUserAgreed()
    {
        using var world = new Gate();

        world.Publish(DisplayIndicatorKind.ReorderPending);

        Assert.Equal(TimeSpan.FromMilliseconds(200), Assert.Single(world.RequestedDelays));
    }

    private sealed class ControllableDelay
    {
        private readonly List<Pending> _pending = [];
        private readonly Lock _sync = new();

        public List<TimeSpan> Requested { get; } = [];

        public Task Delay(TimeSpan duration, CancellationToken token)
        {
            // Default (synchronous) continuations: completing this in Elapse() runs the gate's post-await onset
            // logic inline on the caller, so a test can assert the fired state directly instead of polling for a
            // thread-pool continuation (whose scheduling slipped past the old real-clock deadline under CI load).
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

            foreach (var pending in Take())
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

        public void ElapseIgnoringCancellation()
        {
            RequireInlineContinuations();

            foreach (var pending in Take()) { pending.Completion.TrySetResult(); }
        }

        // Completing a delay's TaskCompletionSource (see Delay) only runs the awaiting gate continuation inline
        // when the ambient host leaves no SynchronizationContext and the default TaskScheduler in place - which
        // the xUnit v3 test host does. Assert it here so any future host change fails loudly at the point of
        // reliance instead of silently reintroducing the timing flakiness this design replaced.
        private static void RequireInlineContinuations()
        {
            Assert.Null(SynchronizationContext.Current);
            Assert.Same(TaskScheduler.Default, TaskScheduler.Current);
        }

        private Pending[] Take()
        {
            lock (_sync)
            {
                Pending[] outstanding = [.. _pending];

                _pending.Clear();

                return outstanding;
            }
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

    private sealed class Gate : IDisposable
    {
        private readonly ControllableDelay _delay = new();
        private readonly DisplayIndicatorGate _gate;
        private readonly FakeSource _source;

        private int _announcements;

        public Gate()
        {
            _source = new FakeSource(PresentationFor(DisplayIndicatorKind.None, revision: 0));
            _gate = new DisplayIndicatorGate(_source, _delay.Delay);
            _gate.OnsetElapsed += () => Interlocked.Increment(ref _announcements);
        }

        public int Announcements => Volatile.Read(ref _announcements);

        public IReadOnlyList<TimeSpan> RequestedDelays => _delay.Requested;

        public long Revision { get; private set; }

        public void Dispose() => _gate.Dispose();

        public void ElapseExpectingNothing()
        {
            int before = Announcements;

            _delay.Elapse();

            Assert.Equal(before, Announcements);
        }

        public void ElapseIgnoringCancellationUntilFired(int expectedAnnouncements)
        {
            int before = Announcements;

            _delay.ElapseIgnoringCancellation();

            Assert.Equal(before + expectedAnnouncements, Announcements);
        }

        public void ElapseUntilFired()
        {
            int before = Announcements;

            _delay.Elapse();

            Assert.True(Announcements > before, "the onset must fire once its delay elapses");
        }

        public bool IsFired(DisplayIndicatorKind kind) => IsFired(kind, Revision);

        public bool IsFired(DisplayIndicatorKind kind, long revision) => _gate.IsFiredFor(kind, revision);

        public long Publish(DisplayIndicatorKind kind)
        {
            Revision++;

            _source.Publish(PresentationFor(kind, Revision));

            return Revision;
        }
    }
}
