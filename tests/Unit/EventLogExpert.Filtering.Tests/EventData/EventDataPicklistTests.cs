// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.TestUtils;

namespace EventLogExpert.Filtering.Tests.EventData;

/// <summary>
///     Field-name and per-field value picklists that feed the Basic editor's EventData row. Exercises the composite
///     <c>(snapshot, fieldName)</c> cache key (R7) so distinct fields keep distinct value lists.
/// </summary>
[Collection("EventPropertyValuesCache")]
public sealed class EventDataPicklistTests : IDisposable
{
    public EventDataPicklistTests() => EventPropertyValuesCache.Clear();

    public void Dispose() => EventPropertyValuesCache.Clear();

    [Fact]
    public void GetEventDataFieldNames_ReturnsDistinctSortedNames()
    {
        var snapshot = new object();
        var events = new[]
        {
            EventDataTestFactory.CreateEventWithData(("TargetUserName", "a"), ("Status", 1L)),
            EventDataTestFactory.CreateEventWithData(("Status", 2L), ("SubjectUserName", "b"))
        };

        var names = EventPropertyValuesCache.GetEventDataFieldNames(snapshot, events);

        Assert.Equal(["Status", "SubjectUserName", "TargetUserName"], names);
    }

    [Fact]
    public void GetEventDataFieldNames_SortsOrdinal()
    {
        var snapshot = new object();
        var events = new[]
        {
            EventDataTestFactory.CreateEventWithData(("b", "1")),
            EventDataTestFactory.CreateEventWithData(("A", "2")),
            EventDataTestFactory.CreateEventWithData(("a", "3"))
        };

        // Ordinal orders uppercase before lowercase ('A'=65, 'a'=97, 'b'=98), unlike a culture-sensitive sort.
        Assert.Equal(["A", "a", "b"], EventPropertyValuesCache.GetEventDataFieldNames(snapshot, events));
    }

    [Fact]
    public void GetEventDataFieldNames_ReturnsParameterLabelsForSyntheticPlaceholders()
    {
        // The synthetic "%1".."%N" names (e.g. CAPI2 4192) reach the field picker as the friendly "Parameter N".
        var snapshot = new object();
        var events = new[] { EventDataTestFactory.CreateEventWithData(("%1", "MsSense.exe"), ("%2", "CodeSigning")) };

        Assert.Equal(["Parameter 1", "Parameter 2"], EventPropertyValuesCache.GetEventDataFieldNames(snapshot, events));
    }

    [Fact]
    public void GetEventDataFieldValues_DifferentFields_ProduceDifferentLists()
    {
        var snapshot = new object();
        var events = new[] { EventDataTestFactory.CreateEventWithData(("A", "1"), ("B", "2")) };

        Assert.Equal(["1"], EventPropertyValuesCache.GetEventDataFieldValues(snapshot, events, "A"));
        Assert.Equal(["2"], EventPropertyValuesCache.GetEventDataFieldValues(snapshot, events, "B"));
    }

    [Fact]
    public void GetEventDataFieldValues_EmptyFieldName_ReturnsEmpty()
    {
        var snapshot = new object();
        var events = new[] { EventDataTestFactory.CreateEventWithData(("A", "1")) };

        Assert.Empty(EventPropertyValuesCache.GetEventDataFieldValues(snapshot, events, string.Empty));
    }

    [Fact]
    public void GetEventDataFieldValues_ReturnsDistinctSortedValues()
    {
        var snapshot = new object();
        var events = new[]
        {
            EventDataTestFactory.CreateEventWithData(("User", "admin")),
            EventDataTestFactory.CreateEventWithData(("User", "guest")),
            EventDataTestFactory.CreateEventWithData(("User", "admin")),
            EventDataTestFactory.CreateEventWithData(("Other", "x"))
        };

        var values = EventPropertyValuesCache.GetEventDataFieldValues(snapshot, events, "User");

        Assert.Equal(["admin", "guest"], values);
    }

    [Fact]
    public void GetEventDataFieldValues_UnknownFieldName_ReturnsEmptyWithoutCaching()
    {
        var snapshot = new object();
        var events = new[] { EventDataTestFactory.CreateEventWithData(("A", "1")) };

        // A name that isn't a real field (as an editable picker would produce mid-typing) is bounded out: it
        // returns empty and is not admitted to the per-snapshot value cache.
        Assert.Empty(EventPropertyValuesCache.GetEventDataFieldValues(snapshot, events, "NotAField"));
        Assert.Equal(["1"], EventPropertyValuesCache.GetEventDataFieldValues(snapshot, events, "A"));
    }
}
