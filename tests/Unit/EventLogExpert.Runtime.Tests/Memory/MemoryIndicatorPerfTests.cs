// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Memory;
using System.Diagnostics;

namespace EventLogExpert.Runtime.Tests.Memory;

/// <summary>
///     Measures the steady-state overhead the advisory memory indicator adds in the normal (non-pressure) scenario.
///     The governor's only per-sample work is a non-collecting managed-heap read; there is no forced GC on the
///     sampling/load path (that is asserted deterministically in <see cref="MemoryIndicatorEffectTests" />). Run with
///     detailed output to capture the numbers for the PR.
/// </summary>
public sealed class MemoryIndicatorPerfTests(ITestOutputHelper output)
{
    private const int Iterations = 200_000;

    [Fact]
    public void PerDispatchWorkingSetCost_IsMeasured()
    {
        var meter = new ProcessMemoryMeter();
        const int Samples = 2_000;

        for (var warmup = 0; warmup < 100; warmup++) { _ = meter.GetWorkingSetBytes(); }

        long sink = 0;
        var stopwatch = Stopwatch.StartNew();

        for (var i = 0; i < Samples; i++) { sink += meter.GetWorkingSetBytes(); }

        stopwatch.Stop();
        double usPerCall = stopwatch.Elapsed.TotalMicroseconds / Samples;

        // Working set is sampled only when a change is dispatched (not every tick), so this cost is incurred rarely.
        output.WriteLine($"GetWorkingSetBytes (per-dispatch only): {usPerCall:F2} us/call over {Samples:N0} reads (sink={sink}).");

        // Wide sanity bound only (Process.WorkingSet64 walks the system process list, so a loaded agent varies widely).
        Assert.True(usPerCall < 50_000, $"per-dispatch working-set read unexpectedly slow: {usPerCall:F2} us/call.");
    }

    [Fact]
    public void PerSampleReadCost_IsNegligibleAndNonCollecting()
    {
        var meter = new ProcessMemoryMeter();

        for (var warmup = 0; warmup < 1_000; warmup++) { _ = meter.GetManagedHeapBytes(); }

        long sink = 0;
        int gen2Before = GC.CollectionCount(2);
        var stopwatch = Stopwatch.StartNew();

        for (var i = 0; i < Iterations; i++) { sink += meter.GetManagedHeapBytes(); }

        stopwatch.Stop();
        int inducedGen2 = GC.CollectionCount(2) - gen2Before;
        double nsPerCall = stopwatch.Elapsed.TotalNanoseconds / Iterations;

        output.WriteLine($"GetManagedHeapBytes: {nsPerCall:F1} ns/call over {Iterations:N0} reads; gen2 induced = {inducedGen2} (sink={sink}).");
        output.WriteLine($"Steady state is one read per second, so the governor adds roughly {nsPerCall:F1} ns of work per second in the normal scenario.");

        // Generous sanity bounds only (the deterministic no-forced-GC proof is in the effect tests); wide enough not to
        // flake on a loaded CI agent.
        Assert.True(nsPerCall < 100_000, $"per-sample read unexpectedly slow: {nsPerCall:F1} ns/call.");
    }
}
