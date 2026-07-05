// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.TestUtils;
using EventLogExpert.Filtering.Parsing;
using EventLogExpert.Filtering.Tests.TestUtils;

namespace EventLogExpert.Filtering.Tests.EventData;

/// <summary>
///     Per-event allocation budget for EventData predicates. Zero allocation is required for the String and packed
///     scalar / Guid field kinds (R4) so an active EventData filter doesn't add GC pressure on a hot virtualization
///     scroll. (Sid / Bytes / array kinds use the documented AsString fallback and are not covered here.)
/// </summary>
public sealed class EventDataAllocationTests
{
    [Fact]
    public void GuidEquality_OverWarmEvent_AllocatesZeroBytes()
    {
        var id = new Guid("11111111-2222-3333-4444-555555555555");

        Assert.True(FilterParser.TryCompile($"EventData[\"Id\"] == \"{id}\"", out var compiled, out var error), error);

        var warmEvent = EventDataTestFactory.CreateEventWithData(("Id", id));

        var bytes = AllocationUtils.MeasurePredicateAllocations(compiled.Predicate, warmEvent);

        Assert.Equal(0, bytes);
    }

    [Theory]
    [InlineData("EventData[\"User\"] == \"admin\"")]
    [InlineData("EventData[\"User\"].Contains(\"adm\", StringComparison.OrdinalIgnoreCase)")]
    [InlineData("EventData[\"Code\"] == \"5\"")]
    [InlineData("(new[] {\"5\", \"6\"}).Contains(EventData[\"Code\"])")]
    public void Predicate_OverWarmEvent_AllocatesZeroBytes(string filter)
    {
        Assert.True(FilterParser.TryCompile(filter, out var compiled, out var error), error);

        var warmEvent = EventDataTestFactory.CreateEventWithData(("User", "admin"), ("Code", 5L));

        var bytes = AllocationUtils.MeasurePredicateAllocations(compiled.Predicate, warmEvent);

        Assert.Equal(0, bytes);
    }
}
