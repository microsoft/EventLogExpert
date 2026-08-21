// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.Memory;
using NSubstitute;
using System.Diagnostics;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Runtime.Tests.Memory;

public sealed class MemoryIndicatorEffectTests
{
    private const long ElevatedBytes = 100;
    private const long HighBytes = 200;

    [Fact]
    public void AfterDispose_TickDoesNotDispatch()
    {
        var harness = new Harness();
        harness.Meter.Heap = 5 * 1024 * 1024;
        var effect = harness.CreateEffect();

        effect.Dispose();
        effect.Dispose(); // idempotent
        effect.Tick();

        Assert.Empty(harness.Dispatched);
    }

    [Fact]
    public void CloseReclaim_IsIssuedExactlyOnce_AfterTheDeadline()
    {
        var harness = new Harness();
        using var effect = harness.CreateEffect();

        _ = effect.HandleCloseAllLogs(harness.Dispatcher);
        harness.Advance(TimeSpan.FromSeconds(2)); // past the 1.5s deadline
        effect.Tick();
        effect.Tick();
        effect.Tick();

        Assert.Equal(1, harness.Meter.ReclaimCount);
    }

    [Fact]
    public void CloseReclaim_IsNotIssuedBeforeTheDeadline()
    {
        var harness = new Harness();
        using var effect = harness.CreateEffect();

        _ = effect.HandleCloseAllLogs(harness.Dispatcher);
        harness.Advance(TimeSpan.FromSeconds(1)); // deadline is 1.5s
        effect.Tick();

        Assert.Equal(0, harness.Meter.ReclaimCount);
    }

    [Fact]
    public void Ctor_Throws_WhenThresholdsAreOutOfOrder()
    {
        var harness = new Harness();

        Assert.Throws<ArgumentException>(() =>
            harness.CreateEffect(elevatedBytes: 200, highBytes: 100));
    }

    [Fact]
    public void DefaultThresholds_DeriveFrom50And75PercentOfAvailableRam()
    {
        var harness = new Harness();
        harness.Meter.Available = 1000; // 50% => 500, 75% => 750
        using var effect = harness.CreateEffect(elevatedBytes: null, highBytes: null);

        // 600 is >= 50% (500) and < 75% (750) of the 1000-byte available RAM, so it is Elevated once the dwell elapses.
        harness.Meter.Heap = 600;
        effect.Tick();
        harness.Advance(TimeSpan.FromSeconds(3));
        effect.Tick();
        Assert.Equal(MemoryUsageLevel.Elevated, harness.Dispatched[^1].Level);

        // 800 crosses 75% (750) -> High.
        harness.Meter.Heap = 800;
        effect.Tick();
        harness.Advance(TimeSpan.FromSeconds(3));
        effect.Tick();
        Assert.Equal(MemoryUsageLevel.High, harness.Dispatched[^1].Level);
    }

    [Fact]
    public void FirstTick_DispatchesCurrentHeapAsNormal()
    {
        var harness = new Harness();
        harness.Meter.Heap = 5 * 1024 * 1024;
        harness.Meter.WorkingSet = 900;
        using var effect = harness.CreateEffect();

        effect.Tick();

        var action = Assert.Single(harness.Dispatched);
        Assert.Equal(5, action.UsedMebibytes);
        Assert.Equal(MemoryUsageLevel.Normal, action.Level);
        Assert.Equal(900, action.WorkingSetBytes);
    }

    [Fact]
    public void HandleStoreInitialized_ArmsTheSelfReschedulingSampler()
    {
        var meter = new IncrementingHeapMeter(); // each read grows the heap so the dedupe never suppresses a tick
        var dispatched = 0;
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.When(call => call.Dispatch(Arg.Any<object>())).Do(_ => Interlocked.Increment(ref dispatched));

        using var effect = new MemoryIndicatorEffect(
            meter,
            dispatcher,
            Substitute.For<ITraceLogger>(),
            sampleInterval: TimeSpan.FromMilliseconds(15),
            elevatedBytes: ElevatedBytes,
            highBytes: HighBytes);

        // Exercises the real arm-on-StoreInitialized AND the self-reschedule (>= 3 requires the finally re-arm to keep
        // firing), which the manual-Tick tests cannot cover.
        _ = effect.HandleStoreInitialized(dispatcher);

        Assert.True(
            SpinWait.SpinUntil(() => Volatile.Read(ref dispatched) >= 3, TimeSpan.FromSeconds(5)),
            $"the sampler did not self-reschedule (dispatch count={Volatile.Read(ref dispatched)}).");
    }

    [Fact]
    public void Level_DoesNotEscalate_WhenTheBandOscillatesWithinTheDwell()
    {
        var harness = new Harness();
        using var effect = harness.CreateEffect();

        harness.Meter.Heap = 150; // Elevated candidate
        effect.Tick();
        harness.Advance(TimeSpan.FromSeconds(1)); // still under the 2s dwell
        harness.Meter.Heap = 0; // back to Normal - resets the candidate
        effect.Tick();
        harness.Advance(TimeSpan.FromSeconds(1));
        harness.Meter.Heap = 150; // Elevated candidate again
        effect.Tick();

        Assert.All(harness.Dispatched, action => Assert.Equal(MemoryUsageLevel.Normal, action.Level));
    }

    [Fact]
    public void Level_EscalatesToElevated_OnlyAfterTheDwellWindow()
    {
        var harness = new Harness();
        harness.Meter.Heap = 150; // between elevated (100) and high (200)
        using var effect = harness.CreateEffect();

        effect.Tick();
        Assert.Equal(MemoryUsageLevel.Normal, harness.Dispatched[^1].Level); // candidate pending, not yet effective

        harness.Advance(TimeSpan.FromSeconds(3)); // past the 2s dwell
        effect.Tick();

        Assert.Equal(MemoryUsageLevel.Elevated, harness.Dispatched[^1].Level);
    }

    [Fact]
    public void Level_EscalatesToHigh_WhenHeapCrossesTheHighThreshold()
    {
        var harness = new Harness();
        harness.Meter.Heap = 250; // above high (200)
        using var effect = harness.CreateEffect();

        effect.Tick();
        harness.Advance(TimeSpan.FromSeconds(3));
        effect.Tick();

        Assert.Equal(MemoryUsageLevel.High, harness.Dispatched[^1].Level);
    }

    [Fact]
    public void Level_StaysNormal_UntilAvailableRamCanBeRead()
    {
        var harness = new Harness();
        harness.Meter.Available = 0; // no load reading yet (no GC has run), so the bands cannot be sized
        harness.Meter.Heap = 1_000_000_000;
        using var effect = harness.CreateEffect(elevatedBytes: null, highBytes: null);

        effect.Tick();
        harness.Advance(TimeSpan.FromSeconds(5));
        effect.Tick();

        Assert.All(harness.Dispatched, action => Assert.Equal(MemoryUsageLevel.Normal, action.Level));
    }

    [Fact]
    public void RepeatedTick_WithSameQuantizedValueAndLevel_DoesNotDispatchAgain()
    {
        var harness = new Harness();
        harness.Meter.Heap = 5 * 1024 * 1024;
        using var effect = harness.CreateEffect();

        effect.Tick();
        effect.Tick();
        effect.Tick();

        Assert.Single(harness.Dispatched);
    }

    [Fact]
    public void Tick_QuantizesHeapToWholeMebibytes()
    {
        var harness = new Harness();
        harness.Meter.Heap = (5 * 1024 * 1024) + (900 * 1024); // 5.9 MiB
        using var effect = harness.CreateEffect();

        effect.Tick();

        Assert.Equal(5, Assert.Single(harness.Dispatched).UsedMebibytes);
    }

    [Fact]
    public void Tick_WithoutAClose_NeverForcesACollection()
    {
        var harness = new Harness();
        using var effect = harness.CreateEffect();

        // The steady-state sampling path only reads the heap; no forced GC ever occurs without an explicit close.
        for (var i = 0; i < 100; i++)
        {
            harness.Meter.Heap = i * 1024 * 1024;
            harness.Advance(TimeSpan.FromSeconds(1));
            effect.Tick();
        }

        Assert.Equal(0, harness.Meter.ReclaimCount);
    }

    [Fact]
    public void UserCloseCompleted_SchedulesReclaimOnce()
    {
        var harness = new Harness();
        using var effect = harness.CreateEffect();

        _ = effect.HandleUserCloseCompleted(
            new LogClosedByUserCompletedAction(EventLogId.Create()), harness.Dispatcher);
        harness.Advance(TimeSpan.FromSeconds(2)); // past the 1.5s deadline
        effect.Tick();
        effect.Tick();

        Assert.Equal(1, harness.Meter.ReclaimCount);
    }

    private sealed class FakeMeter : IProcessMemoryMeter
    {
        public long Available { get; set; }

        public long Heap { get; set; }

        public int ReclaimCount { get; private set; }

        public long WorkingSet { get; set; } = 42;

        public long GetAvailablePhysicalBytes() => Available;

        public long GetManagedHeapBytes() => Heap;

        public long GetWorkingSetBytes() => WorkingSet;

        public void RequestBackgroundReclaim() => ReclaimCount++;
    }

    private sealed class Harness
    {
        private long _now;

        public Harness()
        {
            Dispatcher
                .When(dispatcher => dispatcher.Dispatch(Arg.Any<object>()))
                .Do(call =>
                {
                    if (call.Arg<object>() is MemoryIndicatorRecomputedAction action) { Dispatched.Add(action); }
                });
        }

        public List<MemoryIndicatorRecomputedAction> Dispatched { get; } = [];

        public IDispatcher Dispatcher { get; } = Substitute.For<IDispatcher>();

        public FakeMeter Meter { get; } = new();

        public void Advance(TimeSpan span) => _now += (long)(span.TotalSeconds * Stopwatch.Frequency);

        public MemoryIndicatorEffect CreateEffect(
            long? elevatedBytes = ElevatedBytes,
            long? highBytes = HighBytes) =>
            new(
                Meter,
                Dispatcher,
                Substitute.For<ITraceLogger>(),
                // A far-future interval keeps the internal re-arm from firing a real timer callback during the test;
                // Tick() is driven manually and the dwell/deadline read the injected clock.
                sampleInterval: TimeSpan.FromHours(1),
                closeReclaimDelay: TimeSpan.FromSeconds(1.5),
                levelDwell: TimeSpan.FromSeconds(2),
                elevatedBytes: elevatedBytes,
                highBytes: highBytes,
                monotonicTimestampProvider: () => _now);
    }

    private sealed class IncrementingHeapMeter : IProcessMemoryMeter
    {
        private long _heap;

        public long GetAvailablePhysicalBytes() => 0;

        public long GetManagedHeapBytes() => Interlocked.Add(ref _heap, 1024 * 1024);

        public long GetWorkingSetBytes() => 0;

        public void RequestBackgroundReclaim() { }
    }
}
