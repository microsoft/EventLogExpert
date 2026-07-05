// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Readers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Principal;

namespace EventLogExpert.Eventing.Tests.Common.Events;

public sealed class EventFieldValueTests
{
    [Fact]
    public void AsString_ByteArray_IsHex()
    {
        Assert.Equal("0102", EventFieldValue.FromProperty((EventProperty)(byte[])[1, 2]).AsString());
    }

    [Fact]
    public void AsString_StringArray_IsJoined()
    {
        Assert.Equal("a, b", EventFieldValue.FromProperty((EventProperty)(string[])["a", "b"]).AsString());
    }

    [Fact]
    public void AsString_UsesInvariantCulture()
    {
        CultureInfo prior = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            Assert.Equal("1.5", EventFieldValue.FromProperty(1.5d).AsString());
            Assert.Equal("1.5", EventFieldValue.FromProperty(1.5f).AsString());
        }
        finally
        {
            CultureInfo.CurrentCulture = prior;
        }
    }

    [Fact]
    public void FromProperty_BooleanAndDateTime_Project()
    {
        EventFieldValue boolean = EventFieldValue.FromProperty(true);

        Assert.Equal(EventFieldValueKind.Boolean, boolean.Kind);
        Assert.True(boolean.TryGetBoolean(out bool flag));
        Assert.True(flag);

        var when = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        EventFieldValue timestamp = EventFieldValue.FromProperty(when);

        Assert.Equal(EventFieldValueKind.DateTime, timestamp.Kind);
        Assert.True(timestamp.TryGetDateTime(out DateTime result));
        Assert.Equal(when, result);
    }

    [Fact]
    public void FromProperty_Double_ProjectsToDouble()
    {
        EventFieldValue value = EventFieldValue.FromProperty(1.5d);

        Assert.Equal(EventFieldValueKind.Double, value.Kind);
        Assert.True(value.TryGetDouble(out double result));
        Assert.Equal(1.5d, result);
    }

    [Fact]
    public void FromProperty_IntegerArray_JoinsInvariantInAsString()
    {
        EventFieldValue value = EventFieldValue.FromProperty(EventProperty.FromReference(new uint[] { 1, 2, 3 }));

        Assert.Equal(EventFieldValueKind.Array, value.Kind);
        Assert.Equal("1, 2, 3", value.AsString());
    }

    [Fact]
    public void FromProperty_ReferenceShapes_DisambiguateByRuntimeType()
    {
        Assert.Equal(EventFieldValueKind.String, EventFieldValue.FromProperty((EventProperty)"x").Kind);
        Assert.Equal(EventFieldValueKind.Guid, EventFieldValue.FromProperty(Guid.NewGuid()).Kind);
        Assert.Equal(EventFieldValueKind.Sid, EventFieldValue.FromProperty((EventProperty)new SecurityIdentifier("S-1-5-18")).Kind);
        Assert.Equal(EventFieldValueKind.Bytes, EventFieldValue.FromProperty((EventProperty)(byte[])[1, 2]).Kind);
        Assert.Equal(EventFieldValueKind.StringArray, EventFieldValue.FromProperty((EventProperty)(string[])["a", "b"]).Kind);
        Assert.Equal(EventFieldValueKind.Null, EventFieldValue.FromProperty((EventProperty)(string?)null).Kind);
    }

    [Fact]
    public void FromProperty_SignedIntegrals_ProjectToInt64()
    {
        foreach (EventProperty property in new[]
                 {
                     (sbyte)-5, (short)-5, -5, (EventProperty)(-5L)
                 })
        {
            EventFieldValue value = EventFieldValue.FromProperty(property);

            Assert.Equal(EventFieldValueKind.Int64, value.Kind);
            Assert.True(value.TryGetInt64(out long result));
            Assert.Equal(-5, result);
        }
    }

    [Fact]
    public void FromProperty_Single_StaysSingle_AndIsNotMisreadAsDouble()
    {
        EventFieldValue value = EventFieldValue.FromProperty(1.5f);

        Assert.Equal(EventFieldValueKind.Single, value.Kind);
        Assert.True(value.TryGetSingle(out float single));
        Assert.Equal(1.5f, single);
        Assert.False(value.TryGetDouble(out _));
    }

    [Fact]
    public void FromProperty_UInt64MaxValue_RoundTrips()
    {
        EventFieldValue value = EventFieldValue.FromProperty(ulong.MaxValue);

        Assert.True(value.TryGetUInt64(out ulong result));
        Assert.Equal(ulong.MaxValue, result);
    }

    [Fact]
    public void FromProperty_UnsignedIntegralsAndSizeT_ProjectToUInt64()
    {
        foreach (EventProperty property in new[]
                 {
                     (byte)5, (ushort)5, 5u, 5UL, (EventProperty)(nuint)5
                 })
        {
            EventFieldValue value = EventFieldValue.FromProperty(property);

            Assert.Equal(EventFieldValueKind.UInt64, value.Kind);
            Assert.True(value.TryGetUInt64(out ulong result));
            Assert.Equal(5UL, result);
        }
    }

    [Fact]
    public void Layout_RetainedPropertyIs16Bytes_ProjectionWithinBudget()
    {
        Assert.Equal(16, Unsafe.SizeOf<EventProperty>());
        Assert.True(Unsafe.SizeOf<EventFieldValue>() <= 24);
    }

    [Fact]
    public void TryGetGuid_ReturnsStoredValue()
    {
        var guid = Guid.NewGuid();
        EventFieldValue value = EventFieldValue.FromProperty(guid);

        Assert.True(value.TryGetGuid(out Guid result));
        Assert.Equal(guid, result);
    }
}
