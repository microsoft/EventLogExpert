// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Provider.Resolution;

namespace EventLogExpert.Provider.Tests.Resolution;

public sealed class TemplateSignatureTests
{
    [Fact]
    public void Equal_CountAttributeDiffersOnly_AreEqual()
    {
        // count is render-dead today and intentionally excluded from the signature.
        const string WithoutCount = "<template><data name=\"Items\" inType=\"win:UInt32\"/></template>";
        const string WithCount = "<template><data name=\"Items\" inType=\"win:UInt32\" count=\"8\"/></template>";

        Assert.True(TemplateSignature.Equal(WithoutCount, WithCount));
    }

    [Fact]
    public void Equal_DataElementsWithNoSignatureAttributes_FailClosedAndDoNotCollapse()
    {
        // Non-canonical <data> elements fall back to raw text so distinct inputs do not collapse.
        const string Foo = "<template><data foo=\"1\"/></template>";
        const string Bar = "<template><data bar=\"2\"/></template>";

        Assert.False(TemplateSignature.Equal(Foo, Bar));
    }

    [Fact]
    public void Equal_DataElementsWithOnlyEmptySignatureValues_FailClosedAndDoNotCollapse()
    {
        // Empty signature values fail closed to raw text so distinct inputs do not collapse.
        const string EmptyName = "<template><data name=\"\"/></template>";
        const string EmptyInType = "<template><data inType=\"\"/></template>";

        Assert.False(TemplateSignature.Equal(EmptyName, EmptyInType));
    }

    [Fact]
    public void Equal_DifferentFieldOrder_AreNotEqual()
    {
        const string Ab = "<template><data name=\"A\"/><data name=\"B\"/></template>";
        const string Ba = "<template><data name=\"B\"/><data name=\"A\"/></template>";

        Assert.False(TemplateSignature.Equal(Ab, Ba));
    }

    [Fact]
    public void Equal_DifferentInType_AreNotEqual()
    {
        // inType is kept as provider content even though the formatter ignores it.
        const string AsUInt = "<template><data name=\"V\" inType=\"win:UInt32\" outType=\"xs:string\"/></template>";
        const string AsInt = "<template><data name=\"V\" inType=\"win:Int32\" outType=\"xs:string\"/></template>";

        Assert.False(TemplateSignature.Equal(AsUInt, AsInt));
    }

    [Fact]
    public void Equal_DifferentOutType_AreNotEqual()
    {
        const string AsString = "<template><data name=\"V\" outType=\"xs:string\"/></template>";
        const string AsHex = "<template><data name=\"V\" outType=\"win:HexInt32\"/></template>";

        Assert.False(TemplateSignature.Equal(AsString, AsHex));
    }

    [Fact]
    public void Equal_DistinctNonCanonicalElements_FailClosedAndDoNotCollapse()
    {
        // Single-quoted attributes fall back to raw text so distinct inputs do not collapse.
        const string SingleQuotedA = "<template><data name='A'/></template>";
        const string SingleQuotedB = "<template><data name='B'/></template>";

        Assert.False(TemplateSignature.Equal(SingleQuotedA, SingleQuotedB));
    }

    [Fact]
    public void Equal_FieldsDifferingOnlyByUtf8CollapsingSurrogate_StayInSyncWithTheHashEncoding()
    {
        // Equal must mirror AppendTo's UTF-8 replacement-byte behavior so merge equality and hashing do not drift.
        const string FirstSurrogate = "<template><data name=\"\uD800\"/></template>";
        const string SecondSurrogate = "<template><data name=\"\uD801\"/></template>";

        Assert.True(TemplateSignature.Equal(FirstSurrogate, SecondSurrogate));
    }

    [Fact]
    public void Equal_NullEmptyAndDataLessTemplate_AreEqual()
    {
        Assert.True(TemplateSignature.Equal(default, "".AsSpan()));
        Assert.True(TemplateSignature.Equal(default, "<template></template>".AsSpan()));
    }

    [Fact]
    public void Equal_QuotedAngleBracketInValue_DoesNotTruncateOrCollapseDistinctTemplates()
    {
        // Quoted angle brackets must not terminate the element before later signature attributes are read.
        const string WithString = "<template><data name=\"x/>y\" outType=\"xs:string\"/></template>";
        const string WithHex = "<template><data name=\"x/>y\" outType=\"win:HexInt32\"/></template>";

        Assert.False(TemplateSignature.Equal(WithString, WithHex));
        Assert.False(TemplateSignature.Equal(WithString, "<template><data name=\"x/>y\"/></template>"));
    }

    [Fact]
    public void Equal_StructGroupingDiffersOnly_AreEqual()
    {
        // <struct> grouping is render-dead today; the flat data nodes are the signature.
        const string Flat = "<template><data name=\"A\" outType=\"xs:string\"/><data name=\"B\" outType=\"xs:string\"/></template>";
        const string Grouped = "<template><struct name=\"S\" count=\"2\"><data name=\"A\" outType=\"xs:string\"/><data name=\"B\" outType=\"xs:string\"/></struct></template>";

        Assert.True(TemplateSignature.Equal(Flat, Grouped));
    }

    [Fact]
    public void Equal_WhitespaceAndAttributeOrderDifferAtSameFields_AreEqual()
    {
        const string Compact = "<template><data name=\"User\" inType=\"win:UnicodeString\" outType=\"xs:string\"/></template>";
        const string SpacedAndReordered = "<template>\r\n  <data   outType=\"xs:string\"  inType=\"win:UnicodeString\"   name=\"User\" />\r\n</template>";

        Assert.True(TemplateSignature.Equal(Compact, SpacedAndReordered));
    }

    [Fact]
    public void EventsAreEquivalent_ComparesTemplatesByTheSameSignature()
    {
        // Locks merge equivalence to TemplateSignature so hashing and merging cannot drift.
        const string Compact = "<template><data name=\"User\" outType=\"xs:string\"/></template>";
        const string Spaced = "<template>  <data   name=\"User\"   outType=\"xs:string\" />  </template>";
        const string Different = "<template><data name=\"User\" outType=\"win:HexInt32\"/></template>";

        EventModel baseEvent = MakeEvent(Compact);

        Assert.Equal(TemplateSignature.Equal(Compact, Spaced), ProviderContentMerge.EventsAreEquivalent(baseEvent, MakeEvent(Spaced)));
        Assert.Equal(TemplateSignature.Equal(Compact, Different), ProviderContentMerge.EventsAreEquivalent(baseEvent, MakeEvent(Different)));
        Assert.True(ProviderContentMerge.EventsAreEquivalent(baseEvent, MakeEvent(Spaced)));
        Assert.False(ProviderContentMerge.EventsAreEquivalent(baseEvent, MakeEvent(Different)));

        static EventModel MakeEvent(string template) => new() { Id = 1, Keywords = [], Template = template };
    }
}
