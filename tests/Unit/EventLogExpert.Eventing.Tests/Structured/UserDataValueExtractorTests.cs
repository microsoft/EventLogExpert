// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Structured;

namespace EventLogExpert.Eventing.Tests.Structured;

// Guards the resolve-time value scan: find every nested-UserData value (text + attribute), collapse repeats into one
// multi-value field, key each field as UserDataFieldPath.ToStorageKey would, and flag the event Incomplete when the
// distinct-path cap drops a path (so a filter on a not-found path is kept visible, not decided absent).
public sealed class UserDataValueExtractorTests
{
    private const string Ns = "xmlns='http://schemas.microsoft.com/win/2004/08/events/event'";

    [Fact]
    public void Extract_AttributeValue_IsStoredWithAttributeSuffix()
    {
        var (fields, _) = UserDataValueExtractor.Extract(
            $"<Event {Ns}><UserData><Root><Item value='x'/></Root></UserData></Event>");

        UserDataField field = Assert.Single(fields);
        Assert.Equal("Root/Item/@value", field.Path);
        Assert.Equal(["x"], field.Values);
    }

    // A container element (one with child elements) is not itself a value; only its value-bearing descendants are stored.
    [Fact]
    public void Extract_ContainerElement_IsNotAStoredTextValue()
    {
        var (fields, _) = UserDataValueExtractor.Extract(
            $"<Event {Ns}><UserData><Certificate><SubjectName>foo</SubjectName></Certificate></UserData></Event>");

        UserDataField field = Assert.Single(fields);
        Assert.Equal("Certificate/SubjectName", field.Path);
        Assert.DoesNotContain(fields, f => f.Path == "Certificate");
    }

    // On cap trip the event is flagged Incomplete and new paths dropped, but already-tracked paths keep collecting (the
    // scan does not stop): here A and B are tracked, C is dropped, and A's later value is still collected.
    [Fact]
    public void Extract_DistinctPathCapTrips_FlagsIncompleteButKeepsCollectingTrackedPaths()
    {
        var (fields, incomplete) = UserDataValueExtractor.Extract(
            $"<Event {Ns}><UserData><Root><A>1</A><B>2</B><A>3</A><C>4</C></Root></UserData></Event>",
            maxDistinctPaths: 2);

        Assert.True(incomplete);
        Assert.Equal(2, fields.Length);
        Assert.Equal(["1", "3"], Assert.Single(fields, f => f.Path == "Root/A").Values);
        Assert.Equal(["2"], Assert.Single(fields, f => f.Path == "Root/B").Values);
        Assert.DoesNotContain(fields, f => f.Path == "Root/C");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Extract_NullOrEmptyXml_IsDecisivelyAbsentNotIncomplete(string? xml)
    {
        var (fields, incomplete) = UserDataValueExtractor.Extract(xml);

        Assert.Empty(fields);
        Assert.False(incomplete);
    }

    [Fact]
    public void Extract_PerFieldValueCapExceeded_FlagsFieldTruncated()
    {
        string items = string.Concat(
            Enumerable.Range(0, StructuredFieldPath.MaxWildcardValues + 5).Select(index => $"<Item value='{index}'/>"));

        var (fields, _) = UserDataValueExtractor.Extract(
            $"<Event {Ns}><UserData><Root>{items}</Root></UserData></Event>");

        UserDataField field = Assert.Single(fields);
        Assert.True(field.IsTruncated);
        Assert.Equal(StructuredFieldPath.MaxWildcardValues, field.Values.Length);
    }

    [Fact]
    public void Extract_RepeatedValue_CollapsesIntoOneMultiValueField()
    {
        var (fields, _) = UserDataValueExtractor.Extract(
            $"<Event {Ns}><UserData><Root><Item value='a'/><Item value='b'/><Item value='c'/></Root></UserData></Event>");

        UserDataField field = Assert.Single(fields);
        Assert.Equal("Root/Item/@value", field.Path);
        Assert.Equal(["a", "b", "c"], field.Values);
    }

    [Fact]
    public void Extract_TextValue_IsStoredUnderPlainKey()
    {
        var (fields, incomplete) = UserDataValueExtractor.Extract(
            $"<Event {Ns}><UserData><Root><Status>Success</Status></Root></UserData></Event>");

        Assert.False(incomplete);
        UserDataField field = Assert.Single(fields);
        Assert.Equal("Root/Status", field.Path);
        Assert.Equal(["Success"], field.Values);
        Assert.False(field.IsTruncated);
    }

    [Fact]
    public void Extract_UnderDistinctPathCap_IsNotIncomplete()
    {
        var (_, incomplete) = UserDataValueExtractor.Extract(
            $"<Event {Ns}><UserData><Root><A>1</A><B>2</B></Root></UserData></Event>",
            maxDistinctPaths: 2);

        Assert.False(incomplete);
    }

    // A value longer than the per-value char cap is truncated and its field flagged, so retained memory stays bounded
    // and a comparison against the full value reads it as Unknown (kept visible) not a wrong no-match.
    [Fact]
    public void Extract_ValueExceedingCharCap_IsTruncatedToCapAndFlagged()
    {
        string huge = new('a', UserDataValueExtractor.MaxValueChars + 100);

        var (fields, _) = UserDataValueExtractor.Extract(
            $"<Event {Ns}><UserData><Root><Blob>{huge}</Blob></Root></UserData></Event>");

        UserDataField field = Assert.Single(fields);
        Assert.True(field.IsTruncated);
        Assert.Equal(UserDataValueExtractor.MaxValueChars, field.Values[0].Length);
    }

    [Fact]
    public void Extract_WhitespaceOnlyText_IsNotStored()
    {
        var (fields, _) = UserDataValueExtractor.Extract(
            $"<Event {Ns}><UserData><Root><Blank>   </Blank></Root></UserData></Event>");

        Assert.Empty(fields);
    }
}
