// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Structured;

namespace EventLogExpert.Eventing.Tests.Structured;

// Guards the tri-state contract of the stored-UserData accessor: a found field surfaces its values (truncated if the
// field or event is incomplete), an absent path on a complete event is decisively absent, and an absent path on an
// incomplete event is a present-but-empty truncated result so a filter keeps the row visible.
public sealed class ResolvedEventUserDataTests
{
    [Fact]
    public void TryGetUserDataValues_AbsentPathOnCompleteEvent_IsDecisivelyAbsent()
    {
        StructuredFieldResult result = EventWith(incomplete: false, new UserDataField("Foo/Bar", ["x"], false))
            .TryGetUserDataValues("Other/Path");

        Assert.True(result.IsAbsent);
        Assert.False(result.IsTruncated);
    }

    // An absent path on an Incomplete event must not be decisively absent (that would hide the row under an include
    // filter); it is present-but-empty and truncated so the comparison yields Unknown.
    [Fact]
    public void TryGetUserDataValues_AbsentPathOnIncompleteEvent_IsPresentEmptyAndTruncated()
    {
        StructuredFieldResult result = EventWith(incomplete: true, new UserDataField("Foo/Bar", ["x"], false))
            .TryGetUserDataValues("Other/Path");

        Assert.False(result.IsAbsent);
        Assert.True(result.IsTruncated);
        Assert.Empty(result.PresentValues.ToArray());
    }

    [Fact]
    public void TryGetUserDataValues_EventWithoutUserData_IsDecisivelyAbsent()
    {
        StructuredFieldResult result = new ResolvedEvent("TestLog", LogPathType.Channel).TryGetUserDataValues("Foo/Bar");

        Assert.True(result.IsAbsent);
        Assert.False(result.IsTruncated);
    }

    [Fact]
    public void TryGetUserDataValues_FoundField_ReturnsPresentValues()
    {
        StructuredFieldResult result = EventWith(incomplete: false, new UserDataField("Foo/Bar", ["x", "y"], false))
            .TryGetUserDataValues("Foo/Bar");

        Assert.False(result.IsAbsent);
        Assert.False(result.IsTruncated);
        Assert.Equal(["x", "y"], result.PresentValues.ToArray());
    }

    // A found value on an Incomplete event is still tainted truncated: treating every result on an incomplete event as
    // non-decisive keeps the fail-safe simple (an early scan bail could also have cut this field's tail).
    [Fact]
    public void TryGetUserDataValues_FoundFieldOnIncompleteEvent_IsTaintedTruncated()
    {
        StructuredFieldResult result = EventWith(incomplete: true, new UserDataField("Foo/Bar", ["x"], IsTruncated: false))
            .TryGetUserDataValues("Foo/Bar");

        Assert.False(result.IsAbsent);
        Assert.True(result.IsTruncated);
    }

    [Fact]
    public void TryGetUserDataValues_FoundTruncatedField_IsTruncated()
    {
        StructuredFieldResult result = EventWith(incomplete: false, new UserDataField("Foo/Bar", ["x"], IsTruncated: true))
            .TryGetUserDataValues("Foo/Bar");

        Assert.False(result.IsAbsent);
        Assert.True(result.IsTruncated);
    }

    private static ResolvedEvent EventWith(bool incomplete, params UserDataField[] fields) =>
        new("TestLog", LogPathType.Channel) { UserData = [.. fields], UserDataIncomplete = incomplete };
}
