// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Filtering.Tests.TestUtils;

/// <summary>
///     Helpers that wrap GC-bookkeeping calls so allocation-budget assertions in test bodies stay free of loop /
///     warm-up boilerplate. The warm-up loop runs the predicate often enough to JIT every code path the closure can hit;
///     the measurement loop is large enough that any byte allocated per iteration shows up as a non-zero delta.
/// </summary>
internal static class AllocationUtils
{
    public const int MeasurementIterations = 10_000;
    public const int WarmupIterations = 1_000;

    public static long MeasurePredicateAllocations(Func<ResolvedEvent, bool> predicate, ResolvedEvent evt)
    {
        for (var i = 0; i < WarmupIterations; i++) { predicate(evt); }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var i = 0; i < MeasurementIterations; i++) { predicate(evt); }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
