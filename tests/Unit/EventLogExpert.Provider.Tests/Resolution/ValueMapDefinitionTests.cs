// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Provider.Resolution;

namespace EventLogExpert.Provider.Tests.Resolution;

public sealed class ValueMapDefinitionTests
{
    [Fact]
    public void BitMap_EmptyLeadingName_StillEmitsSeparator()
    {
        // A flag whose manifest message trims to empty yields an empty Name (reachable via
        // EventMessageProvider's TrimEnd after its IsNullOrEmpty guard). The separator must be emitted
        // per match index like string.Join - never leaving an unwritten '\0' in the string.Create buffer.
        var definition = new ValueMapDefinition(
            isBitMap: true,
            entries: [new ValueMapEntry(1, string.Empty), new ValueMapEntry(2, "B")]);

        bool decoded = definition.TryDecode(3u, out string result);

        Assert.True(decoded);
        Assert.Equal(",B", result);
        Assert.DoesNotContain('\0', result);
    }

    [Fact]
    public void BitMap_MultipleFlags_ReturnsCommaJoinedNamesInEntryOrder()
    {
        // 643 = 512 | 128 | 2 | 1 (matches the Kernel-Boot VsmPolicy example).
        var definition = new ValueMapDefinition(
            isBitMap: true,
            entries:
            [
                new ValueMapEntry(1, "VBS Enabled"),
                new ValueMapEntry(2, "VSM Required"),
                new ValueMapEntry(4, "Unused"),
                new ValueMapEntry(128, "Hvci"),
                new ValueMapEntry(512, "Boot Chain Signer Soft Enforced")
            ]);

        bool decoded = definition.TryDecode(643u, out string result);

        Assert.True(decoded);
        Assert.Equal("VBS Enabled,VSM Required,Hvci,Boot Chain Signer Soft Enforced", result);
    }

    [Fact]
    public void BitMap_NoBitsMatch_ReturnsFalse()
    {
        var definition = new ValueMapDefinition(
            isBitMap: true,
            entries: [new ValueMapEntry(1, "A"), new ValueMapEntry(2, "B")]);

        bool decoded = definition.TryDecode(4u, out string result);

        Assert.False(decoded);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void BitMap_PartialMatchWithUndefinedBit_ReturnsMatchedNamesOnly()
    {
        // 5 = 1 | 4; only bit 1 is defined, so (matching EvtFormatMessage) the undefined 0x4 bit is dropped.
        var definition = new ValueMapDefinition(
            isBitMap: true,
            entries: [new ValueMapEntry(1, "A"), new ValueMapEntry(2, "B")]);

        bool decoded = definition.TryDecode(5u, out string result);

        Assert.True(decoded);
        Assert.Equal("A", result);
    }

    [Fact]
    public void BitMap_SingleFlag_ReturnsName()
    {
        var definition = new ValueMapDefinition(
            isBitMap: true,
            entries: [new ValueMapEntry(1, "Enabled"), new ValueMapEntry(2, "Required")]);

        bool decoded = definition.TryDecode(2u, out string result);

        Assert.True(decoded);
        Assert.Equal("Required", result);
    }

    [Fact]
    public void BitMap_Zero_WithoutZeroEntry_ReturnsFalse()
    {
        var definition = new ValueMapDefinition(
            isBitMap: true,
            entries: [new ValueMapEntry(1, "A"), new ValueMapEntry(2, "B")]);

        bool decoded = definition.TryDecode(0u, out string result);

        Assert.False(decoded);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void BitMap_Zero_WithZeroEntry_ReturnsZeroName()
    {
        var definition = new ValueMapDefinition(
            isBitMap: true,
            entries: [new ValueMapEntry(0, "None"), new ValueMapEntry(1, "A")]);

        bool decoded = definition.TryDecode(0u, out string result);

        Assert.True(decoded);
        Assert.Equal("None", result);
    }

    [Fact]
    public void BitMap_ZeroValuedEntry_SkippedForNonZeroInput()
    {
        // A zero-valued flag must not be emitted for a non-zero input (a & 0 == 0 would always "match").
        var definition = new ValueMapDefinition(
            isBitMap: true,
            entries: [new ValueMapEntry(0, "None"), new ValueMapEntry(1, "A")]);

        bool decoded = definition.TryDecode(1u, out string result);

        Assert.True(decoded);
        Assert.Equal("A", result);
    }

    [Fact]
    public void EmptyEntries_ReturnsFalse()
    {
        var definition = new ValueMapDefinition(isBitMap: false, entries: []);

        bool decoded = definition.TryDecode(10u, out string result);

        Assert.False(decoded);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void IsBitMap_ReflectsConstructorArgument()
    {
        Assert.True(new ValueMapDefinition(isBitMap: true, entries: []).IsBitMap);
        Assert.False(new ValueMapDefinition(isBitMap: false, entries: []).IsBitMap);
    }

    [Fact]
    public void ValueMap_ExactMatch_ReturnsName()
    {
        // Matches the Ntfs BusType example: 10 -> SAS.
        var definition = new ValueMapDefinition(
            isBitMap: false,
            entries:
            [
                new ValueMapEntry(9, "iSCSI"),
                new ValueMapEntry(10, "SAS"),
                new ValueMapEntry(11, "SATA")
            ]);

        bool decoded = definition.TryDecode(10u, out string result);

        Assert.True(decoded);
        Assert.Equal("SAS", result);
    }

    [Fact]
    public void ValueMap_IntegralTypes_AreAccepted()
    {
        var definition = new ValueMapDefinition(
            isBitMap: false,
            entries: [new ValueMapEntry(10, "SAS")]);

        Assert.True(definition.TryDecode((byte)10, out string fromByte));
        Assert.Equal("SAS", fromByte);

        Assert.True(definition.TryDecode((short)10, out string fromShort));
        Assert.Equal("SAS", fromShort);

        Assert.True(definition.TryDecode(10, out string fromInt));
        Assert.Equal("SAS", fromInt);

        Assert.True(definition.TryDecode(10UL, out string fromUlong));
        Assert.Equal("SAS", fromUlong);
    }

    [Fact]
    public void ValueMap_NoMatch_ReturnsFalse()
    {
        var definition = new ValueMapDefinition(
            isBitMap: false,
            entries: [new ValueMapEntry(10, "SAS")]);

        bool decoded = definition.TryDecode(99u, out string result);

        Assert.False(decoded);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ValueMap_NonIntegralValue_ReturnsFalse()
    {
        var definition = new ValueMapDefinition(
            isBitMap: false,
            entries: [new ValueMapEntry(10, "SAS")]);

        Assert.False(definition.TryDecode("10", out _));
        Assert.False(definition.TryDecode(null, out _));
        Assert.False(definition.TryDecode(10.0, out _));
    }

    [Fact]
    public void ValueMap_SignedNegativeOne_MatchesUnsignedMaxEntry()
    {
        // A 32-bit -1 must decode against an unsigned 0xFFFFFFFF entry, not sign-extend to 64 bits.
        var definition = new ValueMapDefinition(
            isBitMap: false,
            entries: [new ValueMapEntry(0xFFFFFFFF, "Unknown")]);

        bool decoded = definition.TryDecode(-1, out string result);

        Assert.True(decoded);
        Assert.Equal("Unknown", result);
    }
}
