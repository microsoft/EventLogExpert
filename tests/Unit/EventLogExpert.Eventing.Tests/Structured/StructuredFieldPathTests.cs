// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Structured;

namespace EventLogExpert.Eventing.Tests.Structured;

public sealed class StructuredFieldPathTests
{
    // Samples declare the event default namespace and leave the payload unprefixed, like real rendered event XML, so
    // local-name matching is exercised against the namespace it must ignore.
    private const string Ns = "xmlns='http://schemas.microsoft.com/win/2004/08/events/event'";

    [Fact]
    public void CollectValues_AbsentPath_IsNull()
    {
        StructuredFieldResult result = Collect(
            $"<Event {Ns}><UserData><Root><Item value='x'/></Root></UserData></Event>",
            "Event/UserData/Root/Missing/@value");

        Assert.Equal(EventFieldValueKind.Null, result.Value.Kind);
        Assert.False(result.IsTruncated);
    }

    [Fact]
    public void CollectValues_AttributeValueWithRawGreaterThan_IsExtracted()
    {
        StructuredFieldResult result = Collect(
            $"<Event {Ns}><UserData><Root><Item value='a > b'/></Root></UserData></Event>",
            "Event/UserData/Root/Item/@value");

        Assert.Equal("a > b", result.Value.AsString());
    }

    [Fact]
    public void CollectValues_DecodesAstralNumericEntity()
    {
        StructuredFieldResult result = Collect(
            $"<Event {Ns}><UserData><Root><Item value='&#x1F600;'/></Root></UserData></Event>",
            "Event/UserData/Root/Item/@value");

        Assert.Equal("\U0001F600", result.Value.AsString());
    }

    [Fact]
    public void CollectValues_DecodesEntitiesInValues()
    {
        StructuredFieldResult result = Collect(
            $"<Event {Ns}><UserData><Root><Item value='a &amp; b &lt; c'/></Root></UserData></Event>",
            "Event/UserData/Root/Item/@value");

        Assert.Equal("a & b < c", result.Value.AsString());
    }

    [Fact]
    public void CollectValues_ElementTextLeaf_ReturnsCDataText()
    {
        StructuredFieldResult result = Collect(
            $"<Event {Ns}><UserData><Root><Status><![CDATA[Success &amp; Failure]]></Status></Root></UserData></Event>",
            "Event/UserData/Root/Status");

        Assert.Equal("Success &amp; Failure", result.Value.AsString());
    }

    [Fact]
    public void CollectValues_ElementTextLeaf_ReturnsInnerText()
    {
        StructuredFieldResult result = Collect(
            $"<Event {Ns}><UserData><Root><Status>Success</Status></Root></UserData></Event>",
            "Event/UserData/Root/Status");

        Assert.Equal("Success", result.Value.AsString());
    }

    [Fact]
    public void CollectValues_ExceedingCap_IncludesUpToCapAndFlagsTruncation()
    {
        string items = string.Concat(Enumerable.Range(0, 5).Select(index => $"<Item value='{index}'/>"));
        StructuredFieldResult result = StructuredFieldPath.CollectValues(
            $"<Event {Ns}><UserData><Root>{items}</Root></UserData></Event>".AsSpan(),
            ["Event", "UserData", "Root", "Item"],
            "value",
            cap: 3);

        Assert.Equal("0, 1, 2", result.Value.AsString());
        Assert.True(result.IsTruncated);
    }

    [Fact]
    public void CollectValues_MalformedLeadingEndTag_DoesNotThrowAndStillExtracts()
    {
        StructuredFieldResult result = Collect(
            $"</Stray><Event {Ns}><UserData><Root><Item value='ok'/></Root></UserData></Event>",
            "Event/UserData/Root/Item/@value");

        Assert.Equal("ok", result.Value.AsString());
    }

    [Fact]
    public void CollectValues_MatchesByLocalNameAcrossDefaultNamespace()
    {
        StructuredFieldResult result = Collect(
            $"<Event {Ns}><UserData><Root><Item value='only'/></Root></UserData></Event>",
            "Event/UserData/Root/Item/@value");

        Assert.Equal(EventFieldValueKind.StringArray, result.Value.Kind);
        Assert.Equal("only", result.Value.AsString());
        Assert.False(result.IsTruncated);
    }

    [Fact]
    public void CollectValues_NonPositiveCap_IsAbsent()
    {
        (string[] elements, string? attribute) = StructuredFieldPath.Parse("Event/UserData/Root/Item/@value");

        StructuredFieldResult result = StructuredFieldPath.CollectValues(
            $"<Event {Ns}><UserData><Root><Item value='x'/></Root></UserData></Event>".AsSpan(),
            elements,
            attribute,
            cap: 0);

        Assert.Equal(EventFieldValueKind.Null, result.Value.Kind);
    }

    [Fact]
    public void CollectValues_PresentButEmptyAttribute_IsEmptyString()
    {
        StructuredFieldResult result = Collect(
            $"<Event {Ns}><UserData><Root><Item value=''/></Root></UserData></Event>",
            "Event/UserData/Root/Item/@value");

        Assert.Equal(EventFieldValueKind.StringArray, result.Value.Kind);
        Assert.Equal(string.Empty, result.Value.AsString());
    }

    [Fact]
    public void CollectValues_RepeatingElement_ReturnsAllValuesInDocumentOrder()
    {
        StructuredFieldResult result = Collect(
            $"<Event {Ns}><UserData><Root><Item value='a'/><Item value='b'/><Item value='c'/></Root></UserData></Event>",
            "Event/UserData/Root/Item[*]/@value");

        Assert.Equal("a, b, c", result.Value.AsString());
        Assert.False(result.IsTruncated);
    }

    [Fact]
    public void CollectValues_RepeatingUnderRepeatingParent_ReturnsEveryLeaf()
    {
        StructuredFieldResult result = Collect(
            $"<Event {Ns}><UserData><Root><Item value='a'/></Root><Root><Item value='b'/></Root></UserData></Event>",
            "Event/UserData/Root/Item/@value");

        Assert.Equal("a, b", result.Value.AsString());
    }

    [Theory]
    [InlineData("Event/System/EventID")]
    [InlineData("Event/System/Provider/@Name")]
    [InlineData("Event/UserData/CertVerifyCertificateChainPolicy/Certificate/@subjectName")]
    [InlineData("Event/UserData/Root/Item[*]/@value")]
    [InlineData("Event/UserData/Root/Item[*]")]
    public void IsValidCanonical_AcceptsWellFormedPaths(string path) =>
        Assert.True(StructuredFieldPath.IsValidCanonical(path));

    [Theory]
    [InlineData("")]
    [InlineData("Event//EventID")]
    [InlineData("Event/Sys tem/EventID")]
    [InlineData("Event/System/@Name/Extra")]
    [InlineData("Event/System/@")]
    [InlineData("Event/1System/EventID")]
    [InlineData("@Name")]
    public void IsValidCanonical_RejectsMalformedPaths(string path) =>
        Assert.False(StructuredFieldPath.IsValidCanonical(path));

    [Theory]
    [InlineData("Event/UserData/Root/Item[*]/@value", true)]
    [InlineData("Event/UserData/Root/Item/@value", false)]
    [InlineData("Event/System/EventID", false)]
    public void IsWildcard_DetectsRepeatingMarker(string path, bool expected) =>
        Assert.Equal(expected, StructuredFieldPath.IsWildcard(path));

    [Fact]
    public void Parse_ElementTextLeaf_HasNoAttribute()
    {
        (string[] elements, string? attribute) = StructuredFieldPath.Parse("Event/UserData/Root/Status");

        Assert.Equal(["Event", "UserData", "Root", "Status"], elements);
        Assert.Null(attribute);
    }

    [Fact]
    public void Parse_SplitsElementsAndAttributeAndStripsWildcard()
    {
        (string[] elements, string? attribute) = StructuredFieldPath.Parse("Event/UserData/Root/Item[*]/@value");

        Assert.Equal(["Event", "UserData", "Root", "Item"], elements);
        Assert.Equal("value", attribute);
    }

    private static StructuredFieldResult Collect(string xml, string path)
    {
        (string[] elements, string? attribute) = StructuredFieldPath.Parse(path);

        return StructuredFieldPath.CollectValues(xml.AsSpan(), elements, attribute, StructuredFieldPath.MaxWildcardValues);
    }
}
