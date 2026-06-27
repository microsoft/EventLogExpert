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
        // A <data> element whose attributes are all non-signature (no name/inType/outType/length/map) is non-canonical;
        // it must fall back to its raw substring so two such elements stay distinct instead of collapsing to one
        // all-empty parsed signature.
        const string Foo = "<template><data foo=\"1\"/></template>";
        const string Bar = "<template><data bar=\"2\"/></template>";

        Assert.False(TemplateSignature.Equal(Foo, Bar));
    }

    [Fact]
    public void Equal_DataElementsWithOnlyEmptySignatureValues_FailClosedAndDoNotCollapse()
    {
        // Signature attributes present but all empty-valued carry no render-relevant content; two distinct such elements
        // (a different attribute present, both empty) must still fail closed to raw rather than collapse to one all-empty
        // parsed signature.
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
        // inType is part of provider content even though the formatter ignores it, so it is kept (conservative).
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
        // Single-quoted attributes cannot be canonically extracted, so each element falls back to its raw substring -
        // two genuinely different elements must NOT collapse to one empty signature.
        const string SingleQuotedA = "<template><data name='A'/></template>";
        const string SingleQuotedB = "<template><data name='B'/></template>";

        Assert.False(TemplateSignature.Equal(SingleQuotedA, SingleQuotedB));
    }

    [Fact]
    public void Equal_FieldsDifferingOnlyByUtf8CollapsingSurrogate_StayInSyncWithTheHashEncoding()
    {
        // Equal must mirror AppendTo's UTF-8 encoding so merge equality and the content hash never drift. An unpaired
        // surrogate encodes to the UTF-8 replacement bytes, so two names differing only in such a surrogate collapse to
        // the same bytes - Equal must report them equal too, matching the hash rather than comparing raw UTF-16.
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
        // A '>' or '/>' inside a quoted attribute value must NOT terminate the element early - the trailing
        // attributes must still be read, so templates that differ only after such a value stay distinct.
        const string WithString = "<template><data name=\"x/>y\" outType=\"xs:string\"/></template>";
        const string WithHex = "<template><data name=\"x/>y\" outType=\"win:HexInt32\"/></template>";

        Assert.False(TemplateSignature.Equal(WithString, WithHex));
        Assert.False(TemplateSignature.Equal(WithString, "<template><data name=\"x/>y\"/></template>"));
    }

    [Fact]
    public void Equal_StructGroupingDiffersOnly_AreEqual()
    {
        // <struct> grouping is render-dead today and intentionally excluded; the flat data nodes are the signature.
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
        // Locks the merge equivalence to TemplateSignature so the content hash and the merge can never disagree.
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
