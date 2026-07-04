// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.ProviderMetadata.Wevt;
using System.Buffers.Binary;
using System.Text;

namespace EventLogExpert.Eventing.Tests.ProviderMetadata.Wevt;

public sealed class WevtTemplateReaderTests
{
    private const uint EntryMessageId = 0x2000;
    private const uint EntryValue = 10;
    private const ushort EventId = 170;
    private const int EventTableOffset = 128;
    private const byte EventVersion = 0;
    private const string FieldName = "BusType";
    private const int FieldNameOffset = 384;
    private const string MapName = "BusTypeMap";
    private const int MapNameOffset = 512;
    private const int MapOffset = 448;
    private const int ProviderDataOffset = 64;
    private const int TemplateItemOffset = 320;
    private const int TemplateItemSize = 20;
    private const int TemplateOffset = 256;

    private static readonly Guid s_publisherGuid = new("11112222-3333-4444-5555-666677778888");

    [Fact]
    public void TryParse_BitMap_SetsIsBitMap()
    {
        byte[] resource = BuildResource("BMAP");

        WevtTemplateData? result = WevtTemplateReader.TryParse(resource, s_publisherGuid, logger: null);

        Assert.NotNull(result);
        Assert.True(result!.Maps[MapName].IsBitMap);
    }

    [Fact]
    public void TryParse_EmptyBuffer_ReturnsNull() =>
        Assert.Null(WevtTemplateReader.TryParse([], s_publisherGuid, logger: null));

    [Fact]
    public void TryParse_MapOnStructMember_RecoversFieldAssociation()
    {
        byte[] resource = BuildResource("VMAP", mapOnStructMember: true);

        WevtTemplateData? result = WevtTemplateReader.TryParse(resource, s_publisherGuid, logger: null);

        Assert.NotNull(result);
        Assert.True(result!.EventFieldMaps.TryGetValue(
            new WevtEventKey(EventId, EventVersion),
            out IReadOnlyDictionary<string, string>? fieldMaps));
        Assert.Equal(MapName, fieldMaps![FieldName]);
    }

    [Fact]
    public void TryParse_MapValueCountExceedsCap_ReturnsNull()
    {
        byte[] resource = BuildResource("VMAP");
        BinaryPrimitives.WriteUInt32LittleEndian(resource.AsSpan(MapOffset + 16), uint.MaxValue);

        Assert.Null(WevtTemplateReader.TryParse(resource, s_publisherGuid, logger: null));
    }

    [Fact]
    public void TryParse_TruncatedResource_ReturnsNull()
    {
        byte[] resource = BuildResource("VMAP");

        Assert.Null(WevtTemplateReader.TryParse(resource[..200], s_publisherGuid, logger: null));
    }

    [Fact]
    public void TryParse_UnknownProviderGuid_ReturnsNull()
    {
        byte[] resource = BuildResource("VMAP");

        Assert.Null(WevtTemplateReader.TryParse(resource, Guid.NewGuid(), logger: null));
    }

    [Fact]
    public void TryParse_ValueMap_RecoversEntriesAndFieldAssociation()
    {
        byte[] resource = BuildResource("VMAP");

        WevtTemplateData? result = WevtTemplateReader.TryParse(resource, s_publisherGuid, logger: null);

        Assert.NotNull(result);
        Assert.True(result!.Maps.TryGetValue(MapName, out WevtRawMap? map));
        Assert.False(map!.IsBitMap);
        ValueMapEntryAssertSingle(map, EntryValue, EntryMessageId);

        Assert.True(result.EventFieldMaps.TryGetValue(
            new WevtEventKey(EventId, EventVersion),
            out IReadOnlyDictionary<string, string>? fieldMaps));
        Assert.Equal(MapName, fieldMaps![FieldName]);
    }

    [Fact]
    public void TryParse_WrongRootSignature_ReturnsNull()
    {
        byte[] resource = BuildResource("VMAP");
        resource[0] = (byte)'X';

        Assert.Null(WevtTemplateReader.TryParse(resource, s_publisherGuid, logger: null));
    }

    private static byte[] BuildResource(string mapSignature, bool mapOnStructMember = false)
    {
        byte[] buffer = new byte[560];

        WriteAscii(buffer, 0, "CRIM");
        WriteUInt32(buffer, 12, 1);
        s_publisherGuid.ToByteArray().CopyTo(buffer, 16);
        WriteUInt32(buffer, 32, ProviderDataOffset);

        WriteAscii(buffer, ProviderDataOffset, "WEVT");
        WriteUInt32(buffer, ProviderDataOffset + 12, 1);
        WriteUInt32(buffer, ProviderDataOffset + 20, EventTableOffset);

        WriteAscii(buffer, EventTableOffset, "EVNT");
        WriteUInt32(buffer, EventTableOffset + 8, 1);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(EventTableOffset + 16), EventId);
        buffer[EventTableOffset + 16 + 2] = EventVersion;
        WriteUInt32(buffer, EventTableOffset + 16 + 20, TemplateOffset);

        WriteAscii(buffer, TemplateOffset, "TEMP");
        WriteUInt32(buffer, TemplateOffset + 8, 1);
        WriteUInt32(buffer, TemplateOffset + 12, mapOnStructMember ? 2u : 1u);
        WriteUInt32(buffer, TemplateOffset + 16, TemplateItemOffset);

        // Put the mapped descriptor past itemCount (a struct member) to exercise the full-nameCount scan.
        int mappedItemOffset = mapOnStructMember ? TemplateItemOffset + TemplateItemSize : TemplateItemOffset;

        if (mapOnStructMember)
        {
            WriteUInt32(buffer, TemplateItemOffset + 16, FieldNameOffset);
        }

        WriteUInt32(buffer, mappedItemOffset + 8, MapOffset);
        WriteUInt32(buffer, mappedItemOffset + 16, FieldNameOffset);
        WriteName(buffer, FieldNameOffset, FieldName);

        WriteAscii(buffer, MapOffset, mapSignature);
        WriteUInt32(buffer, MapOffset + 8, MapNameOffset);
        WriteUInt32(buffer, MapOffset + 16, 1);
        WriteUInt32(buffer, MapOffset + 20, EntryValue);
        WriteUInt32(buffer, MapOffset + 24, EntryMessageId);
        WriteName(buffer, MapNameOffset, MapName);

        return buffer;
    }

    private static void ValueMapEntryAssertSingle(WevtRawMap map, uint value, uint messageId)
    {
        WevtRawMapEntry entry = Assert.Single(map.Entries);
        Assert.Equal(value, entry.Value);
        Assert.Equal(messageId, entry.MessageId);
    }

    private static void WriteAscii(byte[] buffer, int offset, string value) =>
        Encoding.ASCII.GetBytes(value).CopyTo(buffer, offset);

    private static void WriteName(byte[] buffer, int offset, string value)
    {
        byte[] characters = Encoding.Unicode.GetBytes(value);
        WriteUInt32(buffer, offset, (uint)(4 + characters.Length));
        characters.CopyTo(buffer, offset + 4);
    }

    private static void WriteUInt32(byte[] buffer, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset), value);
}
