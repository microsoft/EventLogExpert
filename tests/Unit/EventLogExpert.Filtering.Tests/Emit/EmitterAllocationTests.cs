// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Parsing;
using EventLogExpert.Filtering.Tests.TestUtils;

namespace EventLogExpert.Filtering.Tests.Emit;

/// <summary>
///     Per-event allocation budgets for the closure shapes the emitter specializes. The hand-rolled
///     <c>Keywords.Any</c> / <c>(new[]).Contains</c> / <c>P.Contains</c> paths exist to drop per-event allocations to zero
///     on a hot virtualization scroll; any regression that begins allocating defeats their purpose.
/// </summary>
public sealed class EmitterAllocationTests
{
    [Theory]
    [InlineData("Keywords.Any(e => string.Equals(e, \"Audit\", StringComparison.OrdinalIgnoreCase))")]
    [InlineData("Keywords.Any(e => e.Contains(\"audit\", StringComparison.OrdinalIgnoreCase))")]
    [InlineData("Keywords.Any(e => (new[] {\"Audit\", \"System\"}).Contains(e))")]
    [InlineData("(new[] {\"100\", \"200\"}).Contains(Id.ToString())")]
    [InlineData("(new[] {\"TestSource\", \"OtherSource\"}).Contains(Source)")]
    [InlineData("Source.Contains(\"Test\", StringComparison.OrdinalIgnoreCase)")]
    [InlineData("Description.Contains(\"error occurred\", StringComparison.OrdinalIgnoreCase)")]
    [InlineData("Id == 100 && Source == \"TestSource\"")]
    [InlineData("Id == 100 && Source == \"TestSource\" && Level == \"Error\"")]
    // 4-condition AND uses the for-loop fall-through branch; that branch must also be zero-alloc.
    [InlineData(
        "Id == 100 && Source == \"TestSource\" && Level == \"Error\" && ComputerName == \"SERVER01\"")]
    public void Predicate_OverWarmEvent_AllocatesZeroBytesPerInvocation(string filter)
    {
        Assert.True(FilterParser.TryCompile(filter, out var compiled, out var error), error);

        var bytes = AllocationUtils.MeasurePredicateAllocations(compiled.Predicate, FilterTestFixtures.FullyPopulated);

        Assert.Equal(0, bytes);
    }
}
