// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Concurrency;

namespace EventLogExpert.Runtime.Tests.Concurrency;

public sealed class ConcurrencyLimitsTests
{
    [Fact]
    public void MaxBackgroundIoParallelism_StaysWithinTheSafeRange()
    {
        var limit = ConcurrencyLimits.MaxBackgroundIoParallelism;

        // Floored at 1 (Parallel.ForEachAsync / the resolution gate need a positive degree even on a single-core host)
        // and never more than one below the core count (leaves headroom for the UI thread on multi-core hosts).
        Assert.InRange(limit, 1, Math.Max(1, Environment.ProcessorCount - 1));
    }
}
