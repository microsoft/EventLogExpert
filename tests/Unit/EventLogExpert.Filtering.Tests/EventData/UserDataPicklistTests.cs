// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Structured;

namespace EventLogExpert.Filtering.Tests.EventData;

/// <summary>
///     Field-name and per-field value picklists that feed the Basic editor's UserData row. Field names are stored
///     storage keys; values aggregate every stored value of the matching field. Exercises the composite
///     <c>(snapshot, storageKey)</c> cache key and the bounded-membership guard that rejects a mid-typed path.
/// </summary>
[Collection("EventPropertyValuesCache")]
public sealed class UserDataPicklistTests : IDisposable
{
    public UserDataPicklistTests() => EventPropertyValuesCache.Clear();

    public void Dispose() => EventPropertyValuesCache.Clear();

    [Fact]
    public void GetUserDataFieldNames_ReturnsDistinctSortedNames()
    {
        var snapshot = new object();
        var events = new[]
        {
            EventWith(
                new UserDataField("X509Objects/Certificate/SubjectName", ["a"], IsTruncated: false),
                new UserDataField("Status", ["1"], IsTruncated: false)),
            EventWith(
                new UserDataField("Status", ["2"], IsTruncated: false),
                new UserDataField("X509Objects/Certificate/@subjectName", ["b"], IsTruncated: false))
        };

        var names = EventPropertyValuesCache.GetUserDataFieldNames(snapshot, events);

        Assert.Equal(
            ["Status", "X509Objects/Certificate/@subjectName", "X509Objects/Certificate/SubjectName"],
            names);
    }

    [Fact]
    public void GetUserDataFieldNames_SortsOrdinal()
    {
        var snapshot = new object();
        var events = new[]
        {
            EventWith(new UserDataField("b", ["1"], IsTruncated: false)),
            EventWith(new UserDataField("A", ["2"], IsTruncated: false)),
            EventWith(new UserDataField("a", ["3"], IsTruncated: false))
        };

        // Ordinal orders uppercase before lowercase ('A'=65, 'a'=97, 'b'=98), unlike a culture-sensitive sort.
        Assert.Equal(["A", "a", "b"], EventPropertyValuesCache.GetUserDataFieldNames(snapshot, events));
    }

    [Fact]
    public void GetUserDataFieldValues_DifferentFields_ProduceDifferentLists()
    {
        var snapshot = new object();
        var events = new[]
        {
            EventWith(
                new UserDataField("A", ["1"], IsTruncated: false),
                new UserDataField("B", ["2"], IsTruncated: false))
        };

        Assert.Equal(["1"], EventPropertyValuesCache.GetUserDataFieldValues(snapshot, events, "A"));
        Assert.Equal(["2"], EventPropertyValuesCache.GetUserDataFieldValues(snapshot, events, "B"));
    }

    [Fact]
    public void GetUserDataFieldValues_EmptyFieldName_ReturnsEmpty()
    {
        var snapshot = new object();
        var events = new[] { EventWith(new UserDataField("A", ["1"], IsTruncated: false)) };

        Assert.Empty(EventPropertyValuesCache.GetUserDataFieldValues(snapshot, events, string.Empty));
    }

    [Fact]
    public void GetUserDataFieldValues_RepeatingField_ContributesEachCollapsedValue()
    {
        var snapshot = new object();

        // A single repeating field collapses to one field with multiple stored values; each is offered, distinct + sorted.
        var events = new[] { EventWith(new UserDataField("Roles", ["reader", "admin", "reader"], IsTruncated: false)) };

        Assert.Equal(["admin", "reader"], EventPropertyValuesCache.GetUserDataFieldValues(snapshot, events, "Roles"));
    }

    [Fact]
    public void GetUserDataFieldValues_ReturnsDistinctSortedValues()
    {
        var snapshot = new object();
        var events = new[]
        {
            EventWith(new UserDataField("User", ["admin"], IsTruncated: false)),
            EventWith(new UserDataField("User", ["guest"], IsTruncated: false)),
            EventWith(new UserDataField("User", ["admin"], IsTruncated: false)),
            EventWith(new UserDataField("Other", ["x"], IsTruncated: false))
        };

        var values = EventPropertyValuesCache.GetUserDataFieldValues(snapshot, events, "User");

        Assert.Equal(["admin", "guest"], values);
    }

    [Fact]
    public void GetUserDataFieldValues_UnknownFieldName_ReturnsEmptyWithoutCaching()
    {
        var snapshot = new object();
        var events = new[] { EventWith(new UserDataField("A", ["1"], IsTruncated: false)) };

        // A name that isn't a real stored field (mid-typing in an editable picker) is bounded out: returns empty and is not cached.
        Assert.Empty(EventPropertyValuesCache.GetUserDataFieldValues(snapshot, events, "NotAField"));
        Assert.Equal(["1"], EventPropertyValuesCache.GetUserDataFieldValues(snapshot, events, "A"));
    }

    private static ResolvedEvent EventWith(params UserDataField[] fields) =>
        new("TestLog", LogPathType.Channel) { UserData = [.. fields] };
}
