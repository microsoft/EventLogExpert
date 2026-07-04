// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.ProviderMetadata;
using EventLogExpert.Eventing.ProviderMetadata.Wevt;
using EventLogExpert.Provider.Resolution;
using System.Buffers.Binary;
using System.Text;

namespace EventLogExpert.Eventing.Tests.ProviderMetadata.Wevt;

public sealed class OfflineWevtProviderReaderTests
{
    private const int BufferSize = 4096;
    private const int ChannelTableOffset = 512;
    private const int EventTableOffset = 256;
    private const int KeywordTableOffset = 896;
    private const int LevelTableOffset = 1024;
    private const int MapOffset = 1408;
    private const int NameRegionStart = 1600;
    private const int OpcodeTableOffset = 640;
    private const int ProviderOffset = 64;
    private const int TaskTableOffset = 768;
    private const int TemplateOffset = 1152;

    private static readonly Guid s_publisherGuid = new("11112222-3333-4444-5555-666677778888");

    [Fact]
    public void BuildChannels_AuxKeyedInlineOnly_FirstWinsAndUnnamedOmitted()
    {
        // Aux @8 is the reference id; duplicates keep the first row and inline-only unnamed rows are omitted.
        byte[] resource = BuildProviderResource(channels:
        [
            new ChannelSpec(Id: 1, ReferenceId: 16, MessageId: uint.MaxValue, Name: "Operational"),
            new ChannelSpec(Id: 2, ReferenceId: 16, MessageId: uint.MaxValue, Name: "Duplicate"),
            new ChannelSpec(Id: 3, ReferenceId: 99, MessageId: uint.MaxValue, Name: null)
        ]);

        RawProviderContent content = MapResource(resource);

        Assert.Single(content.Channels);
        Assert.Equal("Operational", content.Channels[16]);
        Assert.False(content.Channels.ContainsKey(99));
        Assert.False(content.Channels.ContainsKey(1));
    }

    [Fact]
    public void BuildChannels_NameOffsetOutOfBounds_OmitsChannelWithoutThrowing()
    {
        byte[] resource = BuildProviderResource(channels:
        [
            new ChannelSpec(Id: 1, ReferenceId: 16, MessageId: uint.MaxValue, Name: "Operational")
        ]);

        WriteUInt32(resource, ChannelTableOffset + 12 + 4, 0x7FFFFFF0);

        RawProviderContent content = MapResource(resource);

        Assert.Empty(content.Channels);
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a&b", "a&amp;b")]
    [InlineData("a<b", "a&lt;b")]
    [InlineData("a>b", "a&gt;b")]
    [InlineData("a\"b", "a&quot;b")]
    [InlineData("a'b", "a&apos;b")]
    [InlineData("L'UNKNOWN'", "L&apos;UNKNOWN&apos;")]
    [InlineData("a'&b", "a&apos;&amp;b")]
    public void EscapeXmlAttribute_EscapesEveryXmlSpecialChar(string raw, string expected) =>
        Assert.Equal(expected, WevtTemplateWriter.EscapeXmlAttribute(raw));

    [Fact]
    public void MapToRawContent_FileTimeFlatTemplate_IsNotStructAndWritesFileTime()
    {
        // A multi-field flat template (FILETIME + UInt32) has numNames == numDesc, so it must NOT be treated as a struct.
        byte[] resource = BuildProviderResource(
            events: [new EventSpec(Id: 200, Version: 0, Channel: 0, Level: 0, Opcode: 0, Task: 0, Keywords: 0, MessageId: uint.MaxValue, ReferencesTemplate: true)],
            template: new TemplateSpec(
                Items:
                [
                    new TemplateItemSpec(InType: 0x11, OutType: 0x00, Count: 0, Name: "SystemTime"),
                    new TemplateItemSpec(InType: 0x08, OutType: 0x00, Count: 0, Name: "Count")
                ],
                NameCount: 2));

        RawProviderContent content = MapResource(resource);

        RawProviderEvent mapped = Assert.Single(content.Events);
        Assert.Contains("name=\"SystemTime\"", mapped.Template);
        Assert.Contains("inType=\"win:FILETIME\"", mapped.Template);
        Assert.Contains("name=\"Count\"", mapped.Template);
        Assert.Contains("inType=\"win:UInt32\"", mapped.Template);
    }

    [Fact]
    public void MapToRawContent_FixedCountArray_EmitsLiteralCount()
    {
        // flags@0 bit 0x8 makes count@12 a literal count emitted as count="<n>".
        byte[] resource = BuildProviderResource(
            events: [new EventSpec(Id: 10, Version: 0, Channel: 0, Level: 0, Opcode: 0, Task: 0, Keywords: 0, MessageId: uint.MaxValue, ReferencesTemplate: true)],
            template: new TemplateSpec(
                Items: [new TemplateItemSpec(InType: 0x08, OutType: 0x08, Count: 4, Name: "Parameters", Flags: 0x8)],
                NameCount: 1));

        string template = Assert.Single(MapResource(resource).Events).Template;

        Assert.Contains("name=\"Parameters\"", template);
        Assert.Contains("count=\"4\"", template);
    }

    [Fact]
    public void MapToRawContent_FixedNumericLengthAnsiString_EmitsLength()
    {
        // win:AnsiString (0x02) is length-bearing: a fixed length@14=4 emits length="4".
        byte[] resource = BuildProviderResource(
            events: [new EventSpec(Id: 10, Version: 0, Channel: 0, Level: 0, Opcode: 0, Task: 0, Keywords: 0, MessageId: uint.MaxValue, ReferencesTemplate: true)],
            template: new TemplateSpec(
                Items: [new TemplateItemSpec(InType: 0x02, OutType: 0x00, Count: 0, Name: "Tag", Flags: 0x2, Length: 4)],
                NameCount: 1));

        string template = Assert.Single(MapResource(resource).Events).Template;

        Assert.Contains("inType=\"win:AnsiString\" outType=\"xs:string\" length=\"4\"", template);
    }

    [Fact]
    public void MapToRawContent_FixedNumericLengthBinary_EmitsLength()
    {
        // flags@0 bit 0x2 without bit 0x4 makes Binary length@14 a literal byte count.
        byte[] resource = BuildProviderResource(
            events: [new EventSpec(Id: 10, Version: 0, Channel: 0, Level: 0, Opcode: 0, Task: 0, Keywords: 0, MessageId: uint.MaxValue, ReferencesTemplate: true)],
            template: new TemplateSpec(
                Items: [new TemplateItemSpec(InType: 0x0e, OutType: 0x0f, Count: 0, Name: "Address", Flags: 0x2, Length: 6)],
                NameCount: 1));

        string template = Assert.Single(MapResource(resource).Events).Template;

        Assert.Contains("name=\"Address\"", template);
        Assert.Contains("length=\"6\"", template);
    }

    [Fact]
    public void MapToRawContent_FixedNumericLengthBinaryNonHexBinary_EmitsLength()
    {
        // Fixed Binary length is valid for non-hexBinary outTypes such as IPv6 and must render like native.
        byte[] resource = BuildProviderResource(
            events: [new EventSpec(Id: 10, Version: 0, Channel: 0, Level: 0, Opcode: 0, Task: 0, Keywords: 0, MessageId: uint.MaxValue, ReferencesTemplate: true)],
            template: new TemplateSpec(
                Items: [new TemplateItemSpec(InType: 0x0e, OutType: 0x18, Count: 0, Name: "Address", Flags: 0x2, Length: 16)],
                NameCount: 1));

        string template = Assert.Single(MapResource(resource).Events).Template;

        Assert.Contains("inType=\"win:Binary\" outType=\"win:IPv6\" length=\"16\"", template);
    }

    [Fact]
    public void MapToRawContent_FixedNumericLengthNonBinary_FailsClosedToEmptyTemplate()
    {
        // Fixed numeric lengths are valid only for string and binary inTypes; other inTypes fail closed.
        byte[] resource = BuildProviderResource(
            events: [new EventSpec(Id: 10, Version: 0, Channel: 0, Level: 0, Opcode: 0, Task: 0, Keywords: 0, MessageId: uint.MaxValue, ReferencesTemplate: true)],
            template: new TemplateSpec(
                Items: [new TemplateItemSpec(InType: 0x08, OutType: 0x08, Count: 0, Name: "Value", Flags: 0x2, Length: 6)],
                NameCount: 1));

        RawProviderContent content = MapResource(resource);

        Assert.Equal(string.Empty, Assert.Single(content.Events).Template);
    }

    [Fact]
    public void MapToRawContent_FixedNumericLengthUnicodeString_EmitsLength()
    {
        // win:UnicodeString (0x01) is length-bearing: a fixed length@14=32 emits length="32".
        byte[] resource = BuildProviderResource(
            events: [new EventSpec(Id: 10, Version: 0, Channel: 0, Level: 0, Opcode: 0, Task: 0, Keywords: 0, MessageId: uint.MaxValue, ReferencesTemplate: true)],
            template: new TemplateSpec(
                Items: [new TemplateItemSpec(InType: 0x01, OutType: 0x00, Count: 0, Name: "Hash", Flags: 0x2, Length: 32)],
                NameCount: 1));

        string template = Assert.Single(MapResource(resource).Events).Template;

        Assert.Contains("inType=\"win:UnicodeString\" outType=\"xs:string\" length=\"32\"", template);
    }

    [Fact]
    public void MapToRawContent_LengthFieldReference_EmitsReferencedFieldName()
    {
        // flags@0 bit 0x4 makes length@14 a field index, not a numeric length.
        byte[] resource = BuildProviderResource(
            events: [new EventSpec(Id: 10, Version: 0, Channel: 0, Level: 0, Opcode: 0, Task: 0, Keywords: 0, MessageId: uint.MaxValue, ReferencesTemplate: true)],
            template: new TemplateSpec(
                Items:
                [
                    new TemplateItemSpec(InType: 0x08, OutType: 0x08, Count: 0, Name: "Size"),
                    new TemplateItemSpec(InType: 0x0e, OutType: 0x0f, Count: 0, Name: "Payload", Flags: 0x4, Length: 0)
                ],
                NameCount: 2));

        string template = Assert.Single(MapResource(resource).Events).Template;

        Assert.Contains("name=\"Payload\"", template);
        Assert.Contains("length=\"Size\"", template);
    }

    [Fact]
    public void MapToRawContent_LengthReferenceOutOfRange_FailsClosedToEmptyTemplate()
    {
        // Unresolvable length references fail the whole template closed rather than emitting a guessed length.
        byte[] resource = BuildProviderResource(
            events: [new EventSpec(Id: 10, Version: 0, Channel: 0, Level: 0, Opcode: 0, Task: 0, Keywords: 0, MessageId: uint.MaxValue, ReferencesTemplate: true)],
            template: new TemplateSpec(
                Items: [new TemplateItemSpec(InType: 0x0e, OutType: 0x0f, Count: 0, Name: "Payload", Flags: 0x4, Length: 5)],
                NameCount: 1));

        RawProviderContent content = MapResource(resource);

        Assert.Equal(string.Empty, Assert.Single(content.Events).Template);
    }

    [Fact]
    public void MapToRawContent_NestedStructMember_FailsClosedToEmptyTemplate()
    {
        // Nested structs are absent from the corpus, so memberCount@6 > 0 fails the whole template closed.
        byte[] resource = BuildProviderResource(
            events: [new EventSpec(Id: 7, Version: 0, Channel: 0, Level: 0, Opcode: 0, Task: 0, Keywords: 0, MessageId: 0, ReferencesTemplate: true)],
            template: new TemplateSpec(
                Items:
                [
                    new TemplateItemSpec(InType: 0x00, OutType: 0x00, Count: 0, Name: "Outer", MemberCount: 2, MemberStart: 1),
                    new TemplateItemSpec(InType: 0x00, OutType: 0x00, Count: 0, Name: "Inner", MemberCount: 1, MemberStart: 2),
                    new TemplateItemSpec(InType: 0x08, OutType: 0x00, Count: 0, Name: "Leaf")
                ],
                NameCount: 3,
                ItemCount: 1));

        RawProviderContent content = MapResource(resource);

        Assert.Equal(string.Empty, Assert.Single(content.Events).Template);
    }

    [Fact]
    public void MapToRawContent_OpcodeValue_PassesHighWordThroughForFactoryProjection()
    {
        // OPCO values are already high-word shifted, so the reader preserves the raw native value for factory projection.
        byte[] resource = BuildProviderResource(opcodes:
        [
            new IdentifiedSpec(Id: 0, MessageId: uint.MaxValue, Name: "Op0"),
            new IdentifiedSpec(Id: 1u << 16, MessageId: uint.MaxValue, Name: "Op1"),
            new IdentifiedSpec(Id: 10u << 16, MessageId: uint.MaxValue, Name: "Op10"),
            new IdentifiedSpec(Id: (uint)ushort.MaxValue << 16, MessageId: uint.MaxValue, Name: "OpMax")
        ]);

        WevtProviderData data = ParseResource(resource);
        RawProviderContent content = OfflineWevtProviderReader.MapToRawContent(
            data, s_publisherGuid, "TestProvider", string.Empty, static _ => null);

        Assert.Equal(4, content.Opcodes.Count);
        Assert.Equal(0UL, content.Opcodes[0].Value);
        Assert.Equal(1UL << 16, content.Opcodes[1].Value);
        Assert.Equal(10UL << 16, content.Opcodes[2].Value);
        Assert.Equal((ulong)ushort.MaxValue << 16, content.Opcodes[3].Value);

        ProviderDetails details = ProviderDetailsFactory.Create(content, data.Templates, logger: null);

        Assert.Equal(4, details.Opcodes.Count);
        Assert.Equal("Op0", details.Opcodes[0]);
        Assert.Equal("Op1", details.Opcodes[1]);
        Assert.Equal("Op10", details.Opcodes[10]);
        Assert.Equal("OpMax", details.Opcodes[ushort.MaxValue]);
    }

    [Fact]
    public void MapToRawContent_RenderingCriticalOutTypeBytes_EmitExactLiveCasing()
    {
        // outType spelling is casing-sensitive against the live API: win:Hresult and win:NTStatus are pinned here.
        byte[] resource = BuildProviderResource(
            events: [new EventSpec(Id: 10, Version: 0, Channel: 0, Level: 0, Opcode: 0, Task: 0, Keywords: 0, MessageId: uint.MaxValue, ReferencesTemplate: true)],
            template: new TemplateSpec(
                Items:
                [
                    new TemplateItemSpec(InType: 0x07, OutType: 0x1f, Count: 0, Name: "Status"),
                    new TemplateItemSpec(InType: 0x07, OutType: 0x20, Count: 0, Name: "Result")
                ],
                NameCount: 2));

        string template = Assert.Single(MapResource(resource).Events).Template;

        Assert.Contains("outType=\"win:NTStatus\"", template);
        Assert.Contains("outType=\"win:Hresult\"", template);
        Assert.DoesNotContain("win:HResult", template);
        Assert.DoesNotContain("win:NTSTATUS", template);
    }

    [Fact]
    public void MapToRawContent_StructTemplate_EmitsNestedDataInsideStruct()
    {
        // memberCount@6 claims appended descriptors by range; no count flag means the struct carries no count.
        byte[] resource = BuildProviderResource(
            events: [new EventSpec(Id: 5, Version: 0, Channel: 0, Level: 0, Opcode: 0, Task: 0, Keywords: 0, MessageId: 0, ReferencesTemplate: true)],
            template: new TemplateSpec(
                Items:
                [
                    new TemplateItemSpec(InType: 0x00, OutType: 0x00, Count: 0, Name: "Header", MemberCount: 2, MemberStart: 1),
                    new TemplateItemSpec(InType: 0x08, OutType: 0x00, Count: 0, Name: "Version"),
                    new TemplateItemSpec(InType: 0x08, OutType: 0x00, Count: 0, Name: "Size")
                ],
                NameCount: 3,
                ItemCount: 1));

        RawProviderContent content = MapResource(resource);
        RawProviderEvent mapped = Assert.Single(content.Events);

        Assert.Equal(
            "<template xmlns=\"http://schemas.microsoft.com/win/2004/08/events\">" +
            "<struct name=\"Header\">" +
            "<data name=\"Version\" inType=\"win:UInt32\" outType=\"xs:unsignedInt\"/>" +
            "<data name=\"Size\" inType=\"win:UInt32\" outType=\"xs:unsignedInt\"/>" +
            "</struct></template>",
            mapped.Template);
    }

    [Fact]
    public void MapToRawContent_TaskAndKeyword_UseIdentityValues()
    {
        byte[] resource = BuildProviderResource(
            tasks: [new IdentifiedSpec(Id: 7, MessageId: uint.MaxValue, Name: "Task7")],
            keywords:
            [
                new KeywordSpec(Mask: 0x8000000000000000, MessageId: uint.MaxValue, Name: "KeywordHigh"),
                new KeywordSpec(Mask: 0x0000000000000002, MessageId: uint.MaxValue, Name: "KeywordLow")
            ]);

        WevtProviderData data = ParseResource(resource);
        RawProviderContent content = OfflineWevtProviderReader.MapToRawContent(
            data, s_publisherGuid, "TestProvider", string.Empty, static _ => null);

        Assert.Equal(7UL, Assert.Single(content.Tasks).Value);
        Assert.Equal([0x8000000000000000, 0x0000000000000002], content.Keywords.Select(static keyword => keyword.Value));

        ProviderDetails details = ProviderDetailsFactory.Create(content, data.Templates, logger: null);

        Assert.Equal("Task7", details.Tasks[7]);
        Assert.Equal("KeywordHigh", details.Keywords[unchecked((long)0x8000000000000000)]);
        Assert.Equal("KeywordLow", details.Keywords[2L]);
    }

    [Fact]
    public void MapToRawContent_TemplateItemNameOffsetOutOfBounds_FailsClosedToEmptyTemplate()
    {
        byte[] resource = BuildProviderResource(
            events: [new EventSpec(Id: 10, Version: 0, Channel: 0, Level: 0, Opcode: 0, Task: 0, Keywords: 0, MessageId: uint.MaxValue, ReferencesTemplate: true)],
            template: new TemplateSpec(
                Items: [new TemplateItemSpec(InType: 0x08, OutType: 0x08, Count: 0, Name: "Field")],
                NameCount: 1));

        // Unreadable item names make the whole template fail closed rather than emit partial XML.
        WriteUInt32(resource, TemplateOffset + 20 + 16, BufferSize + 0x100);

        RawProviderContent content = MapResource(resource);

        Assert.Equal(string.Empty, Assert.Single(content.Events).Template);
    }

    [Theory]
    [InlineData(0x7FFFFFF0u)]
    [InlineData(0xFFFFFFF0u)]
    [InlineData(0x7FFFFFFFu)]
    public void MapToRawContent_TemplateItemsPointerOverflow_FailsClosedWithoutThrowing(uint itemsOffset)
    {
        byte[] resource = BuildProviderResource(
            events: [new EventSpec(Id: 10, Version: 0, Channel: 0, Level: 0, Opcode: 0, Task: 0, Keywords: 0, MessageId: uint.MaxValue, ReferencesTemplate: true)],
            template: new TemplateSpec(
                Items: [new TemplateItemSpec(InType: 0x08, OutType: 0x08, Count: 0, Name: "Field")],
                NameCount: 1));

        WriteUInt32(resource, TemplateOffset + 16, itemsOffset);

        RawProviderContent content = MapResource(resource);

        Assert.Equal(string.Empty, Assert.Single(content.Events).Template);
    }

    [Fact]
    public void MapToRawContent_TemplateWithUnclaimedAppendedDescriptor_FailsClosedToEmptyTemplate()
    {
        // Unclaimed appended descriptors fail the exact-partition check and suppress XML output.
        byte[] resource = BuildProviderResource(
            events: [new EventSpec(Id: 10, Version: 0, Channel: 0, Level: 0, Opcode: 0, Task: 0, Keywords: 0, MessageId: uint.MaxValue, ReferencesTemplate: true)],
            template: new TemplateSpec(
                Items: [new TemplateItemSpec(InType: 0x08, OutType: 0x00, Count: 0, Name: "Field1")],
                NameCount: 2));

        RawProviderContent content = MapResource(resource);
        RawProviderEvent mapped = Assert.Single(content.Events);

        Assert.Equal(string.Empty, mapped.Template);
    }

    [Fact]
    public void MapToRawContent_TruncatedTemplateItemDescriptor_FailsClosedToEmptyTemplate()
    {
        byte[] resource = BuildProviderResource(
            events: [new EventSpec(Id: 10, Version: 0, Channel: 0, Level: 0, Opcode: 0, Task: 0, Keywords: 0, MessageId: uint.MaxValue, ReferencesTemplate: true)],
            template: new TemplateSpec(
                Items: [new TemplateItemSpec(InType: 0x08, OutType: 0x08, Count: 0, Name: "Field")],
                NameCount: 1));

        // Truncated descriptors fail the whole template closed instead of writing from partially-read bytes.
        byte[] truncated = resource[..(TemplateOffset + 20 + 4)];

        RawProviderContent content = MapResource(truncated);

        Assert.Equal(string.Empty, Assert.Single(content.Events).Template);
    }

    [Fact]
    public void MapToRawContent_TwoSiblingStructs_EmitEachWithItsOwnMembers()
    {
        // Sibling structs claim disjoint member ranges from their own start indexes, not a shared cursor.
        byte[] resource = BuildProviderResource(
            events: [new EventSpec(Id: 6, Version: 0, Channel: 0, Level: 0, Opcode: 0, Task: 0, Keywords: 0, MessageId: 0, ReferencesTemplate: true)],
            template: new TemplateSpec(
                Items:
                [
                    new TemplateItemSpec(InType: 0x00, OutType: 0x00, Count: 0, Name: "First", MemberCount: 2, MemberStart: 2),
                    new TemplateItemSpec(InType: 0x00, OutType: 0x00, Count: 0, Name: "Second", MemberCount: 2, MemberStart: 4),
                    new TemplateItemSpec(InType: 0x08, OutType: 0x00, Count: 0, Name: "A"),
                    new TemplateItemSpec(InType: 0x08, OutType: 0x00, Count: 0, Name: "B"),
                    new TemplateItemSpec(InType: 0x08, OutType: 0x00, Count: 0, Name: "C"),
                    new TemplateItemSpec(InType: 0x08, OutType: 0x00, Count: 0, Name: "D")
                ],
                NameCount: 6,
                ItemCount: 2));

        RawProviderContent content = MapResource(resource);

        Assert.Equal(
            "<template xmlns=\"http://schemas.microsoft.com/win/2004/08/events\">" +
            "<struct name=\"First\">" +
            "<data name=\"A\" inType=\"win:UInt32\" outType=\"xs:unsignedInt\"/>" +
            "<data name=\"B\" inType=\"win:UInt32\" outType=\"xs:unsignedInt\"/>" +
            "</struct>" +
            "<struct name=\"Second\">" +
            "<data name=\"C\" inType=\"win:UInt32\" outType=\"xs:unsignedInt\"/>" +
            "<data name=\"D\" inType=\"win:UInt32\" outType=\"xs:unsignedInt\"/>" +
            "</struct></template>",
            Assert.Single(content.Events).Template);
    }

    [Fact]
    public void MapToRawContent_UnknownInTypeByte_FailsClosedToEmptyTemplate()
    {
        // Unknown inType bytes fail closed instead of emitting guessed winmeta tokens.
        byte[] resource = BuildProviderResource(
            events: [new EventSpec(Id: 10, Version: 0, Channel: 0, Level: 0, Opcode: 0, Task: 0, Keywords: 0, MessageId: uint.MaxValue, ReferencesTemplate: true)],
            template: new TemplateSpec(
                Items: [new TemplateItemSpec(InType: 0x7e, OutType: 0x01, Count: 0, Name: "Field")],
                NameCount: 1));

        RawProviderContent content = MapResource(resource);

        Assert.Equal(string.Empty, Assert.Single(content.Events).Template);
    }

    [Fact]
    public void MapToRawContent_UnknownOutTypeByte_FailsClosedToEmptyTemplate()
    {
        // Unknown non-zero outType bytes fail closed instead of emitting guessed winmeta tokens.
        byte[] resource = BuildProviderResource(
            events: [new EventSpec(Id: 10, Version: 0, Channel: 0, Level: 0, Opcode: 0, Task: 0, Keywords: 0, MessageId: uint.MaxValue, ReferencesTemplate: true)],
            template: new TemplateSpec(
                Items: [new TemplateItemSpec(InType: 0x08, OutType: 0x7f, Count: 0, Name: "Field")],
                NameCount: 1));

        RawProviderContent content = MapResource(resource);

        Assert.Equal(string.Empty, Assert.Single(content.Events).Template);
    }

    [Fact]
    public void MapToRawContent_VariableCountArray_EmitsReferencedFieldName()
    {
        // flags@0 bit 0x10 makes count@12 a field index, not a numeric count.
        byte[] resource = BuildProviderResource(
            events: [new EventSpec(Id: 10, Version: 0, Channel: 0, Level: 0, Opcode: 0, Task: 0, Keywords: 0, MessageId: uint.MaxValue, ReferencesTemplate: true)],
            template: new TemplateSpec(
                Items:
                [
                    new TemplateItemSpec(InType: 0x08, OutType: 0x08, Count: 0, Name: "ElementCount"),
                    new TemplateItemSpec(InType: 0x08, OutType: 0x08, Count: 0, Name: "Elements", Flags: 0x10)
                ],
                NameCount: 2));

        string template = Assert.Single(MapResource(resource).Events).Template;

        Assert.Contains("name=\"Elements\"", template);
        Assert.Contains("count=\"ElementCount\"", template);
    }

    [Fact]
    public void MapToRawContent_VariableCountReferenceOutOfRange_FailsClosedToEmptyTemplate()
    {
        // Unresolvable count references fail the whole template closed rather than emitting a guessed count.
        byte[] resource = BuildProviderResource(
            events: [new EventSpec(Id: 10, Version: 0, Channel: 0, Level: 0, Opcode: 0, Task: 0, Keywords: 0, MessageId: uint.MaxValue, ReferencesTemplate: true)],
            template: new TemplateSpec(
                Items: [new TemplateItemSpec(InType: 0x08, OutType: 0x08, Count: 5, Name: "Elements", Flags: 0x10)],
                NameCount: 1));

        RawProviderContent content = MapResource(resource);

        Assert.Equal(string.Empty, Assert.Single(content.Events).Template);
    }

    [Fact]
    public void MapToRawContent_VariableCountReferenceToStruct_FailsClosedToEmptyTemplate()
    {
        // Variable counts must reference leaf numeric fields, never structs.
        byte[] resource = BuildProviderResource(
            events: [new EventSpec(Id: 8, Version: 0, Channel: 0, Level: 0, Opcode: 0, Task: 0, Keywords: 0, MessageId: 0, ReferencesTemplate: true)],
            template: new TemplateSpec(
                Items:
                [
                    new TemplateItemSpec(InType: 0x00, OutType: 0x00, Count: 0, Name: "Block", MemberCount: 2, MemberStart: 2),
                    new TemplateItemSpec(InType: 0x08, OutType: 0x08, Count: 0, Name: "Items", Flags: 0x10),
                    new TemplateItemSpec(InType: 0x08, OutType: 0x00, Count: 0, Name: "M1"),
                    new TemplateItemSpec(InType: 0x08, OutType: 0x00, Count: 0, Name: "M2")
                ],
                NameCount: 4,
                ItemCount: 2));

        RawProviderContent content = MapResource(resource);

        Assert.Equal(string.Empty, Assert.Single(content.Events).Template);
    }

    [Fact]
    public void MapToRawContent_ZeroOutTypeByte_EmitsInTypeDefaultOutType()
    {
        // Zero outType uses the inType default because the live API always emits an outType.
        byte[] resource = BuildProviderResource(
            events: [new EventSpec(Id: 10, Version: 0, Channel: 0, Level: 0, Opcode: 0, Task: 0, Keywords: 0, MessageId: uint.MaxValue, ReferencesTemplate: true)],
            template: new TemplateSpec(
                Items: [new TemplateItemSpec(InType: 0x08, OutType: 0x00, Count: 0, Name: "Count")],
                NameCount: 1));

        string template = Assert.Single(MapResource(resource).Events).Template;

        Assert.Contains("inType=\"win:UInt32\"", template);
        Assert.Contains("outType=\"xs:unsignedInt\"", template);
    }

    [Theory]
    [InlineData("%%1000", "Allocate buffer.")]
    [InlineData("X %%1001 Y", "X Second Y")]
    [InlineData("%%1001 %%1001", "Second Second")]
    [InlineData("%%2", "WithTerminator")]
    [InlineData("%1 %%1001", "%1 Second")]
    [InlineData("%%9999", "%%9999")]
    [InlineData("%%%1001", "%%%1001")]
    [InlineData("no markers here", "no markers here")]
    [InlineData("%%4294967295", "NegativeCast")]
    public void ResolveParameterReferences_ResolvesAndTrimsParameterTokens(string raw, string expected) =>
        Assert.Equal(expected, OfflineWevtProviderReader.ResolveParameterReferences(raw, FakeParameterText));

    [Fact]
    public void TryParse_MapLessProvider_ReturnsNullWhileFullParseReturnsEventsAndTables()
    {
        byte[] resource = BuildProviderResource(
            events: [new EventSpec(Id: 1, Version: 0, Channel: 0, Level: 0, Opcode: 0, Task: 0, Keywords: 0, MessageId: uint.MaxValue)],
            opcodes: [new IdentifiedSpec(Id: 1, MessageId: uint.MaxValue, Name: "OpcodeName")]);

        Assert.Null(WevtTemplateReader.TryParse(resource, s_publisherGuid, logger: null));

        // Full parse must still yield events and tables when the legacy map API returns null for map-less providers.
        WevtProviderData data = ParseResource(resource);
        Assert.Single(data.Events);
        Assert.Single(data.Opcodes);
        Assert.Empty(data.Templates.Maps);
    }

    [Fact]
    public void TryParseProvider_ClassicAndModernShareSixteenBitId_ProduceDistinctIds()
    {
        // Two rows share the 16-bit code 4: one classic (projected to the qualified id) and one modern (kept bare).
        byte[] resource = BuildProviderResource(
            events:
            [
                new EventSpec(Id: 4, Version: 90, Channel: 0, Level: 0, Opcode: 0x42, Task: 0, Keywords: 0, MessageId: 0x425A0004),
                new EventSpec(Id: 4, Version: 0, Channel: 0, Level: 0, Opcode: 0, Task: 0, Keywords: 0, MessageId: uint.MaxValue, OpcodeTableReference: 0x40)
            ]);

        WevtProviderData data = ParseResource(resource);

        Assert.Equal(2, data.Events.Count);
        Assert.Contains(data.Events, e => e.Id == 0x425A0004u);
        Assert.Contains(data.Events, e => e is { Id: 4u, Version: 0 });
    }

    [Fact]
    public void TryParseProvider_ClassicEvent_ProjectsToQualifiedIdWithZeroedVersionAndOpcode()
    {
        byte[] resource = BuildProviderResource(
            events: [new EventSpec(Id: 4, Version: 90, Channel: 0, Level: 0, Opcode: 0x42, Task: 0, Keywords: 0, MessageId: 0x425A0004)]);

        WevtProviderEvent parsed = Assert.Single(ParseResource(resource).Events);

        Assert.Equal(0x425A0004u, parsed.Id);
        Assert.Equal((byte)0, parsed.Version);
        Assert.Equal((byte)0, parsed.Opcode);
        Assert.Equal(0x425A0004u, parsed.MessageId);
    }

    [Fact]
    public void TryParseProvider_ClassicEventHighOpcodeByte_ComposesQualifiedIdWithoutSignExtension()
    {
        // An opcode byte >= 0x80 must shift in the uint domain; an int-domain shift would sign-extend into the high dword.
        byte[] resource = BuildProviderResource(
            events: [new EventSpec(Id: 1, Version: 6, Channel: 0, Level: 0, Opcode: 0xC0, Task: 0, Keywords: 0, MessageId: 0xC0060001)]);

        WevtProviderEvent parsed = Assert.Single(ParseResource(resource).Events);

        Assert.Equal(0xC0060001u, parsed.Id);
    }

    [Fact]
    public void TryParseProvider_ClassicEventWithValueMap_KeysFieldMapByQualifiedId()
    {
        // Classic event value maps are theoretical, but map keys must still use the projected event identity.
        byte[] resource = BuildProviderResource(
            events: [new EventSpec(Id: 4, Version: 90, Channel: 0, Level: 0, Opcode: 0x42, Task: 0, Keywords: 0, MessageId: 0x425A0004, ReferencesTemplate: true)],
            template: new TemplateSpec(
                Items: [new TemplateItemSpec(InType: 0x08, OutType: 0x00, Count: 0, Name: "field", HasMap: true)],
                NameCount: 1,
                Map: new MapSpec(Name: "MyMap", IsBitMap: false, Entries: [(Value: 1, MessageId: 0x5000)])));

        WevtProviderData data = ParseResource(resource);

        Assert.True(data.Templates.EventFieldMaps.ContainsKey(new WevtEventKey(0x425A0004, 0)));
        Assert.False(data.Templates.EventFieldMaps.ContainsKey(new WevtEventKey(4, 90)));
    }

    [Fact]
    public void TryParseProvider_FullTables_ReadEveryFieldAtItsOffset()
    {
        byte[] resource = BuildProviderResource(
            events: [new EventSpec(Id: 0x1234, Version: 5, Channel: 0x10, Level: 0x04, Opcode: 0x09, Task: 0x0203, Keywords: 0x0102030405060708, MessageId: 0x0A0B0C0D, OpcodeTableReference: 0x40)],
            channels: [new ChannelSpec(Id: 1, ReferenceId: 200, MessageId: uint.MaxValue, Name: "ChannelName")],
            opcodes: [new IdentifiedSpec(Id: 300, MessageId: uint.MaxValue, Name: "OpcodeName")],
            tasks: [new IdentifiedSpec(Id: 400, MessageId: uint.MaxValue, Name: "TaskName")],
            keywords: [new KeywordSpec(Mask: 0x4000000000000000, MessageId: uint.MaxValue, Name: "KeywordName")]);

        WevtProviderData data = ParseResource(resource);

        WevtProviderEvent parsed = Assert.Single(data.Events);
        Assert.Equal(0x1234u, parsed.Id);
        Assert.Equal((byte)5, parsed.Version);
        Assert.Equal((byte)0x10, parsed.Channel);
        Assert.Equal((byte)0x04, parsed.Level);
        Assert.Equal((byte)0x09, parsed.Opcode);
        Assert.Equal((ushort)0x0203, parsed.Task);
        Assert.Equal(0x0102030405060708u, parsed.Keywords);
        Assert.Equal(0x0A0B0C0Du, parsed.MessageId);
        Assert.Null(parsed.Template);

        WevtChannelEntry channel = Assert.Single(data.Channels);
        Assert.Equal(1u, channel.Id);
        Assert.Equal(200u, channel.ReferenceId);
        Assert.Equal("ChannelName", channel.InlineName);

        Assert.Equal(300u, Assert.Single(data.Opcodes).Id);
        Assert.Equal(400u, Assert.Single(data.Tasks).Id);
        Assert.Equal(0x4000000000000000u, Assert.Single(data.Keywords).Mask);
    }

    [Fact]
    public void TryParseProvider_KeywordCountExceedsCap_FailsClosedToEmptyTable()
    {
        byte[] resource = BuildProviderResource(
            opcodes: [new IdentifiedSpec(Id: 1, MessageId: uint.MaxValue, Name: "OpcodeName")],
            keywords: [new KeywordSpec(Mask: 0x1, MessageId: uint.MaxValue, Name: "KeywordName")]);

        // A count above the table cap is malformed; the keyword table fails closed to empty while the other tables parse.
        WriteUInt32(resource, KeywordTableOffset + 8, uint.MaxValue);

        WevtProviderData data = ParseResource(resource);

        Assert.Empty(data.Keywords);
        Assert.Single(data.Opcodes);
    }

    [Fact]
    public void TryParseProvider_MessageIdCustomerBitSet_LeavesEventUnprojected()
    {
        // A modern manifest message id sets the customer bit (0x20000000); such an event is not a classic row.
        byte[] resource = BuildProviderResource(
            events: [new EventSpec(Id: 4, Version: 90, Channel: 0, Level: 0, Opcode: 0x42, Task: 0, Keywords: 0, MessageId: 0x625A0004)]);

        WevtProviderEvent parsed = Assert.Single(ParseResource(resource).Events);

        Assert.Equal(4u, parsed.Id);
        Assert.Equal((byte)90, parsed.Version);
        Assert.Equal((byte)0x42, parsed.Opcode);
    }

    [Fact]
    public void TryParseProvider_OpcodeTableReferenceNonZero_LeavesEventUnprojected()
    {
        // A non-zero opcode-table reference marks a modern event even when trailing fields look classic.
        byte[] resource = BuildProviderResource(
            events: [new EventSpec(Id: 4, Version: 90, Channel: 0, Level: 0, Opcode: 0x42, Task: 0, Keywords: 0, MessageId: 0x425A0004, OpcodeTableReference: 0x40)]);

        WevtProviderEvent parsed = Assert.Single(ParseResource(resource).Events);

        Assert.Equal(4u, parsed.Id);
        Assert.Equal((byte)90, parsed.Version);
        Assert.Equal((byte)0x42, parsed.Opcode);
    }

    [Fact]
    public void TryParseProvider_TrailingFieldNonZero_LeavesEventUnprojected()
    {
        byte[] resource = BuildProviderResource(
            events: [new EventSpec(Id: 4, Version: 90, Channel: 0, Level: 0, Opcode: 0x42, Task: 0, Keywords: 0, MessageId: 0x425A0004, TrailingField: 1)]);

        WevtProviderEvent parsed = Assert.Single(ParseResource(resource).Events);

        Assert.Equal(4u, parsed.Id);
        Assert.Equal((byte)90, parsed.Version);
        Assert.Equal((byte)0x42, parsed.Opcode);
    }

    [Fact]
    public void Write_XmlEscapedFieldName_StillMatchesMapInjection()
    {
        // Map injection must escape raw field keys identically to the written template.
        byte[] resource = BuildProviderResource(
            events: [new EventSpec(Id: 10, Version: 0, Channel: 0, Level: 0, Opcode: 0, Task: 0, Keywords: 0, MessageId: uint.MaxValue, ReferencesTemplate: true)],
            template: new TemplateSpec(
                Items: [new TemplateItemSpec(InType: 0x08, OutType: 0x00, Count: 0, Name: "a&b", HasMap: true)],
                NameCount: 1,
                Map: new MapSpec(Name: "MyMap", IsBitMap: false, Entries: [(Value: 1, MessageId: 0x5000)])));

        WevtProviderData data = ParseResource(resource);

        Assert.True(data.Templates.EventFieldMaps.TryGetValue(
            new WevtEventKey(10, 0), out IReadOnlyDictionary<string, string>? fieldMaps));
        Assert.Equal("MyMap", fieldMaps!["a&b"]);

        RawProviderContent content = OfflineWevtProviderReader.MapToRawContent(
            data, s_publisherGuid, "TestProvider", string.Empty, static id => id == 0x5000 ? "DecodedOne" : null);

        Assert.Contains("name=\"a&amp;b\"", Assert.Single(content.Events).Template);

        ProviderDetails details = ProviderDetailsFactory.Create(content, data.Templates, logger: null);

        Assert.True(details.Maps.ContainsKey("MyMap"));
        Assert.Contains("map=\"MyMap\"", Assert.Single(details.Events).Template);
    }

    private static byte[] BuildProviderResource(
        EventSpec[]? events = null,
        ChannelSpec[]? channels = null,
        IdentifiedSpec[]? opcodes = null,
        IdentifiedSpec[]? tasks = null,
        KeywordSpec[]? keywords = null,
        TemplateSpec? template = null)
    {
        events ??= [];
        channels ??= [];
        opcodes ??= [];
        tasks ??= [];
        keywords ??= [];

        byte[] buffer = new byte[BufferSize];
        int nameCursor = NameRegionStart;

        int Alloc(string? value)
        {
            if (value is null) { return 0; }

            byte[] characters = Encoding.Unicode.GetBytes(value);
            int offset = nameCursor;
            WriteUInt32(buffer, offset, (uint)(4 + characters.Length));
            characters.CopyTo(buffer, offset + 4);
            nameCursor = (offset + 4 + characters.Length + 3) & ~3;

            return offset;
        }

        WriteAscii(buffer, 0, "CRIM");
        WriteUInt32(buffer, 12, 1);
        s_publisherGuid.ToByteArray().CopyTo(buffer, 16);
        WriteUInt32(buffer, 32, ProviderOffset);

        WriteAscii(buffer, ProviderOffset, "WEVT");
        WriteUInt32(buffer, ProviderOffset + 12, 6);
        int[] elementOffsets = [EventTableOffset, ChannelTableOffset, LevelTableOffset, OpcodeTableOffset, TaskTableOffset, KeywordTableOffset];

        for (int index = 0; index < elementOffsets.Length; index++)
        {
            WriteUInt32(buffer, ProviderOffset + 20 + (index * 8), (uint)elementOffsets[index]);
        }

        WriteAscii(buffer, EventTableOffset, "EVNT");
        WriteUInt32(buffer, EventTableOffset + 8, (uint)events.Length);

        for (int index = 0; index < events.Length; index++)
        {
            EventSpec spec = events[index];
            int entry = EventTableOffset + 16 + (index * 48);
            WriteUInt16(buffer, entry, spec.Id);
            buffer[entry + 2] = spec.Version;
            buffer[entry + 3] = spec.Channel;
            buffer[entry + 4] = spec.Level;
            buffer[entry + 5] = spec.Opcode;
            WriteUInt16(buffer, entry + 6, spec.Task);
            WriteUInt64(buffer, entry + 8, spec.Keywords);
            WriteUInt32(buffer, entry + 16, spec.MessageId);
            WriteUInt32(buffer, entry + 20, spec.ReferencesTemplate ? (uint)TemplateOffset : 0);
            WriteUInt32(buffer, entry + 24, spec.OpcodeTableReference);
            WriteUInt32(buffer, entry + 44, spec.TrailingField);
        }

        WriteAscii(buffer, ChannelTableOffset, "CHAN");
        WriteUInt32(buffer, ChannelTableOffset + 8, (uint)channels.Length);

        for (int index = 0; index < channels.Length; index++)
        {
            ChannelSpec spec = channels[index];
            int entry = ChannelTableOffset + 12 + (index * 16);
            WriteUInt32(buffer, entry, spec.Id);
            WriteUInt32(buffer, entry + 4, (uint)Alloc(spec.Name));
            WriteUInt32(buffer, entry + 8, spec.ReferenceId);
            WriteUInt32(buffer, entry + 12, spec.MessageId);
        }

        WriteAscii(buffer, LevelTableOffset, "LEVL");
        WriteUInt32(buffer, LevelTableOffset + 8, 0);

        WriteAscii(buffer, OpcodeTableOffset, "OPCO");
        WriteUInt32(buffer, OpcodeTableOffset + 8, (uint)opcodes.Length);

        for (int index = 0; index < opcodes.Length; index++)
        {
            IdentifiedSpec spec = opcodes[index];
            int entry = OpcodeTableOffset + 12 + (index * 12);
            WriteUInt32(buffer, entry, spec.Id);
            WriteUInt32(buffer, entry + 4, spec.MessageId);
            WriteUInt32(buffer, entry + 8, (uint)Alloc(spec.Name));
        }

        WriteAscii(buffer, TaskTableOffset, "TASK");
        WriteUInt32(buffer, TaskTableOffset + 8, (uint)tasks.Length);

        for (int index = 0; index < tasks.Length; index++)
        {
            IdentifiedSpec spec = tasks[index];
            int entry = TaskTableOffset + 12 + (index * 28);
            WriteUInt32(buffer, entry, spec.Id);
            WriteUInt32(buffer, entry + 4, spec.MessageId);
            WriteUInt32(buffer, entry + 24, (uint)Alloc(spec.Name));
        }

        WriteAscii(buffer, KeywordTableOffset, "KEYW");
        WriteUInt32(buffer, KeywordTableOffset + 8, (uint)keywords.Length);

        for (int index = 0; index < keywords.Length; index++)
        {
            KeywordSpec spec = keywords[index];
            int entry = KeywordTableOffset + 12 + (index * 16);
            WriteUInt64(buffer, entry, spec.Mask);
            WriteUInt32(buffer, entry + 8, spec.MessageId);
            WriteUInt32(buffer, entry + 12, (uint)Alloc(spec.Name));
        }

        if (template is not null)
        {
            WriteAscii(buffer, TemplateOffset, "TEMP");
            WriteUInt32(buffer, TemplateOffset + 8, (uint)(template.ItemCount ?? template.Items.Length));
            WriteUInt32(buffer, TemplateOffset + 12, (uint)template.NameCount);
            WriteUInt32(buffer, TemplateOffset + 16, TemplateOffset + 20);

            for (int index = 0; index < template.Items.Length; index++)
            {
                TemplateItemSpec item = template.Items[index];
                int itemOffset = TemplateOffset + 20 + (index * 20);
                WriteUInt32(buffer, itemOffset, item.Flags);

                if (item.MemberCount > 0)
                {
                    // A struct descriptor carries no inType: @4 is the member-start index (u16) and @6 the member count.
                    WriteUInt16(buffer, itemOffset + 4, item.MemberStart);
                    WriteUInt16(buffer, itemOffset + 6, item.MemberCount);
                }
                else
                {
                    buffer[itemOffset + 4] = item.InType;
                    buffer[itemOffset + 5] = item.OutType;
                }

                WriteUInt16(buffer, itemOffset + 12, item.Count);
                WriteUInt16(buffer, itemOffset + 14, item.Length);
                WriteUInt32(buffer, itemOffset + 16, (uint)Alloc(item.Name));

                if (item.HasMap)
                {
                    WriteUInt32(buffer, itemOffset + 8, MapOffset);
                }
            }

            if (template.Map is { } map)
            {
                WriteAscii(buffer, MapOffset, map.IsBitMap ? "BMAP" : "VMAP");
                WriteUInt32(buffer, MapOffset + 8, (uint)Alloc(map.Name));
                WriteUInt32(buffer, MapOffset + 16, (uint)map.Entries.Length);

                for (int index = 0; index < map.Entries.Length; index++)
                {
                    int entry = MapOffset + 20 + (index * 8);
                    WriteUInt32(buffer, entry, map.Entries[index].Value);
                    WriteUInt32(buffer, entry + 4, map.Entries[index].MessageId);
                }
            }
        }

        return buffer;
    }

    private static string? FakeParameterText(int rawId) => rawId switch
    {
        1000 => "Allocate buffer.\r\n",
        1001 => "Second",
        2 => "WithTerminator%0",
        -1 => "NegativeCast",
        _ => null
    };

    private static RawProviderContent MapResource(byte[] resource) =>
        OfflineWevtProviderReader.MapToRawContent(
            ParseResource(resource), s_publisherGuid, "TestProvider", string.Empty, static _ => null);

    private static WevtProviderData ParseResource(byte[] resource)
    {
        WevtProviderData? data = WevtTemplateReader.TryParseProvider(resource, s_publisherGuid, logger: null);
        Assert.NotNull(data);

        return data!;
    }

    private static void WriteAscii(byte[] buffer, int offset, string value) =>
        Encoding.ASCII.GetBytes(value).CopyTo(buffer, offset);

    private static void WriteUInt16(byte[] buffer, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset), value);

    private static void WriteUInt32(byte[] buffer, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset), value);

    private static void WriteUInt64(byte[] buffer, int offset, ulong value) =>
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(offset), value);

    private sealed record ChannelSpec(uint Id, uint ReferenceId, uint MessageId, string? Name);

    private sealed record EventSpec(
        ushort Id,
        byte Version,
        byte Channel,
        byte Level,
        byte Opcode,
        ushort Task,
        ulong Keywords,
        uint MessageId,
        bool ReferencesTemplate = false,
        uint OpcodeTableReference = 0,
        uint TrailingField = 0);

    private sealed record IdentifiedSpec(uint Id, uint MessageId, string? Name);

    private sealed record KeywordSpec(ulong Mask, uint MessageId, string? Name);

    private sealed record MapSpec(string Name, bool IsBitMap, (uint Value, uint MessageId)[] Entries);

    private sealed record TemplateItemSpec(
        byte InType,
        byte OutType,
        ushort Count,
        string Name,
        bool HasMap = false,
        uint Flags = 0,
        ushort Length = 0,
        ushort MemberCount = 0,
        ushort MemberStart = 0);

    private sealed record TemplateSpec(TemplateItemSpec[] Items, int NameCount, MapSpec? Map = null, int? ItemCount = null);
}
