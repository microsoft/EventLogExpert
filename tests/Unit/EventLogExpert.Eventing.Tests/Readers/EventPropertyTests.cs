// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Readers;
using System.Security.Principal;

namespace EventLogExpert.Eventing.Tests.Readers;

public sealed class EventPropertyTests
{
    [Fact]
    public void Equals_ComparesKindAndValue()
    {
        Assert.Equal((EventProperty)5, (EventProperty)5);
        Assert.Equal((EventProperty)"abc", (EventProperty)"abc");

        // Same bit value but different Kind (Int32 vs UInt32) must not compare equal.
        Assert.NotEqual((EventProperty)5, (EventProperty)5u);
        Assert.NotEqual((EventProperty)5, (EventProperty)6);
        Assert.NotEqual((EventProperty)"abc", (EventProperty)"xyz");
    }

    [Fact]
    public void Kind_ReflectsTheConstructingType()
    {
        Assert.Equal(EventPropertyKind.SByte, ((EventProperty)(sbyte)1).Kind);
        Assert.Equal(EventPropertyKind.Byte, ((EventProperty)(byte)1).Kind);
        Assert.Equal(EventPropertyKind.Int16, ((EventProperty)(short)1).Kind);
        Assert.Equal(EventPropertyKind.UInt16, ((EventProperty)(ushort)1).Kind);
        Assert.Equal(EventPropertyKind.Int32, ((EventProperty)1).Kind);
        Assert.Equal(EventPropertyKind.UInt32, ((EventProperty)1u).Kind);
        Assert.Equal(EventPropertyKind.Int64, ((EventProperty)1L).Kind);
        Assert.Equal(EventPropertyKind.UInt64, ((EventProperty)1UL).Kind);
        Assert.Equal(EventPropertyKind.Single, ((EventProperty)1.0f).Kind);
        Assert.Equal(EventPropertyKind.Double, ((EventProperty)1.0d).Kind);
        Assert.Equal(EventPropertyKind.Boolean, ((EventProperty)true).Kind);
        Assert.Equal(EventPropertyKind.DateTime, ((EventProperty)new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)).Kind);
        Assert.Equal(EventPropertyKind.SizeT, ((EventProperty)(nuint)1).Kind);
        Assert.Equal(EventPropertyKind.Reference, ((EventProperty)"text").Kind);
        Assert.Equal(EventPropertyKind.Reference, ((EventProperty)(byte[])[1, 2]).Kind);
        Assert.Equal(EventPropertyKind.Reference, ((EventProperty)Guid.Empty).Kind);
        Assert.Equal(EventPropertyKind.Reference, ((EventProperty)new SecurityIdentifier("S-1-5-18")).Kind);
        Assert.Equal(EventPropertyKind.Reference, EventProperty.FromReference(new uint[] { 1 }).Kind);
    }

    [Fact]
    public void TryGetUnsignedBits_CrossWidthIntegrals_ExtractEqualNativeValue()
    {
        // The manifest valueMap contract treats (byte)10, (short)10, 10, 10UL, ... as the same key.
        Assert.Equal(10UL, Bits((byte)10));
        Assert.Equal(10UL, Bits((sbyte)10));
        Assert.Equal(10UL, Bits((short)10));
        Assert.Equal(10UL, Bits((ushort)10));
        Assert.Equal(10UL, Bits(10));
        Assert.Equal(10UL, Bits(10u));
        Assert.Equal(10UL, Bits(10L));
        Assert.Equal(10UL, Bits(10UL));
    }

    [Fact]
    public void TryGetUnsignedBits_NonIntegralKinds_ReturnFalse()
    {
        Assert.False(((EventProperty)1.5f).TryGetUnsignedBits(out _));
        Assert.False(((EventProperty)1.5d).TryGetUnsignedBits(out _));
        Assert.False(((EventProperty)(nuint)10).TryGetUnsignedBits(out _));
        Assert.False(((EventProperty)true).TryGetUnsignedBits(out _));
        Assert.False(((EventProperty)new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TryGetUnsignedBits(out _));
        Assert.False(((EventProperty)"10").TryGetUnsignedBits(out _));
        Assert.False(((EventProperty)(byte[])[1, 2]).TryGetUnsignedBits(out _));
    }

    [Fact]
    public void TryGetUnsignedBits_SignedNegative_MasksToNativeUnsignedWidth()
    {
        Assert.Equal(0xFFUL, Bits((sbyte)-1));
        Assert.Equal(0xFFFFUL, Bits((short)-1));
        Assert.Equal(0xFFFFFFFFUL, Bits(-1));
        Assert.Equal(0xFFFFFFFFFFFFFFFFUL, Bits(-1L));
    }

    private static ulong Bits(EventProperty property)
    {
        Assert.True(property.TryGetUnsignedBits(out ulong bits));

        return bits;
    }
}
