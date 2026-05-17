// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Parsing;
using EventLogExpert.Filtering.Tests.TestUtils;

namespace EventLogExpert.Filtering.Tests;

/// <summary>
///     Per-event allocation budgets for the closure shapes the emitter specializes (per N-D4). The hand-rolled
///     <c>Keywords.Any</c> / <c>(new[]).Contains</c> / <c>P.Contains</c> paths replace LINQ enumerators specifically to
///     drop per-event allocations to zero on a hot virtualization scroll; if any of these begins allocating, the
///     replacement no longer earns its keep over the Dynamic.Core baseline.
/// </summary>
public sealed class EmitterAllocationTests
{
    [Theory]
    // Keywords.Any equality/contains/match-any-of (the original LINQ enumerator suspects).
    [InlineData("Keywords.Any(e => string.Equals(e, \"Audit\", StringComparison.OrdinalIgnoreCase))")]
    [InlineData("Keywords.Any(e => e.Contains(\"audit\", StringComparison.OrdinalIgnoreCase))")]
    [InlineData("Keywords.Any(e => (new[] {\"Audit\", \"System\"}).Contains(e))")]
    // (new[] {...}).Contains(P.ToString()) — the int / string MultiEquals specializations.
    [InlineData("(new[] {\"100\", \"200\"}).Contains(Id.ToString())")]
    [InlineData("(new[] {\"TestSource\", \"OtherSource\"}).Contains(Source)")]
    // string.Contains(string, StringComparison) — should not allocate per call.
    [InlineData("Source.Contains(\"Test\", StringComparison.OrdinalIgnoreCase)")]
    [InlineData("Description.Contains(\"error occurred\", StringComparison.OrdinalIgnoreCase)")]
    // 2 / 3 condition AND specializations (closure flattening).
    [InlineData("Id == 100 && Source == \"TestSource\"")]
    [InlineData("Id == 100 && Source == \"TestSource\" && Level == \"Error\"")]
    // 4-condition AND falls through to the for-loop branch — must still be zero-alloc.
    [InlineData(
        "Id == 100 && Source == \"TestSource\" && Level == \"Error\" && ComputerName == \"SERVER01\"")]
    public void Predicate_OverWarmEvent_AllocatesZeroBytesPerInvocation(string filter)
    {
        Assert.True(FilterParser.TryCompile(filter, out var compiled, out var error), error);

        var bytes = AllocationUtils.MeasurePredicateAllocations(compiled.Predicate, EventUtils.FullyPopulated);

        Assert.Equal(0, bytes);
    }
}
