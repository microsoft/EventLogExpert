// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Readers;
using System.Globalization;
using System.Security.Principal;

namespace EventLogExpert.Eventing.Common.Events;

public enum EventFieldValueKind : byte
{
    String,
    Int64,
    UInt64,
    Double,
    Single,
    Boolean,
    DateTime,
    Guid,
    Sid,
    Bytes,
    StringArray,
    Array,
    Null
}

/// <summary>
///     A single named EventData field value, projected without boxing from the internal rendered property: numeric /
///     bool / DateTime / Single kinds read from a packed 64-bit field, reference shapes (string, Guid, SID, byte[],
///     string[]) from an object slot. Transient (materialized on read from <see cref="EventDataView" />), never retained.
/// </summary>
public readonly struct EventFieldValue
{
    private readonly object? _reference;
    private readonly long _bits;
    private readonly EventFieldValueKind _kind;

    private EventFieldValue(EventFieldValueKind kind, long bits, object? reference)
    {
        _kind = kind;
        _bits = bits;
        _reference = reference;
    }

    public EventFieldValueKind Kind => _kind;

    public bool TryGetInt64(out long value)
    {
        if (_kind == EventFieldValueKind.Int64) { value = _bits; return true; }

        value = 0;

        return false;
    }

    public bool TryGetUInt64(out ulong value)
    {
        if (_kind == EventFieldValueKind.UInt64) { value = unchecked((ulong)_bits); return true; }

        value = 0;

        return false;
    }

    public bool TryGetDouble(out double value)
    {
        if (_kind == EventFieldValueKind.Double) { value = BitConverter.Int64BitsToDouble(_bits); return true; }

        value = 0;

        return false;
    }

    public bool TryGetSingle(out float value)
    {
        if (_kind == EventFieldValueKind.Single) { value = BitConverter.Int32BitsToSingle((int)_bits); return true; }

        value = 0;

        return false;
    }

    public bool TryGetBoolean(out bool value)
    {
        if (_kind == EventFieldValueKind.Boolean) { value = _bits != 0; return true; }

        value = false;

        return false;
    }

    public bool TryGetDateTime(out DateTime value)
    {
        if (_kind == EventFieldValueKind.DateTime) { value = DateTime.FromBinary(_bits); return true; }

        value = default;

        return false;
    }

    public bool TryGetGuid(out Guid value)
    {
        if (_kind == EventFieldValueKind.Guid && _reference is Guid guid) { value = guid; return true; }

        value = default;

        return false;
    }

    public string AsString() => _kind switch
    {
        EventFieldValueKind.String => _reference as string ?? string.Empty,
        EventFieldValueKind.Int64 => _bits.ToString(CultureInfo.InvariantCulture),
        EventFieldValueKind.UInt64 => unchecked((ulong)_bits).ToString(CultureInfo.InvariantCulture),
        EventFieldValueKind.Double => BitConverter.Int64BitsToDouble(_bits).ToString(CultureInfo.InvariantCulture),
        EventFieldValueKind.Single => BitConverter.Int32BitsToSingle((int)_bits).ToString(CultureInfo.InvariantCulture),
        EventFieldValueKind.Boolean => (_bits != 0) ? bool.TrueString : bool.FalseString,
        EventFieldValueKind.DateTime => DateTime.FromBinary(_bits).ToString("O", CultureInfo.InvariantCulture),
        EventFieldValueKind.Guid => ((Guid)_reference!).ToString("D", CultureInfo.InvariantCulture),
        EventFieldValueKind.Sid => ((SecurityIdentifier)_reference!).Value,
        EventFieldValueKind.Bytes => Convert.ToHexString((byte[])_reference!),
        EventFieldValueKind.StringArray => string.Join(", ", (string[])_reference!),
        EventFieldValueKind.Array => JoinArray((Array)_reference!),
        _ => string.Empty
    };

    public override string ToString() => AsString();

    private static string JoinArray(Array array)
    {
        var parts = new string[array.Length];

        for (int i = 0; i < array.Length; i++)
        {
            parts[i] = Convert.ToString(array.GetValue(i), CultureInfo.InvariantCulture) ?? string.Empty;
        }

        return string.Join(", ", parts);
    }

    internal static EventFieldValue FromProperty(in EventProperty property)
    {
        switch (property.Kind)
        {
            case EventPropertyKind.SByte:
            case EventPropertyKind.Int16:
            case EventPropertyKind.Int32:
            case EventPropertyKind.Int64:
                return new(EventFieldValueKind.Int64, property.AsInt64, null);
            case EventPropertyKind.Byte:
            case EventPropertyKind.UInt16:
            case EventPropertyKind.UInt32:
            case EventPropertyKind.UInt64:
            case EventPropertyKind.SizeT:
                return new(EventFieldValueKind.UInt64, unchecked((long)property.AsUInt64), null);
            case EventPropertyKind.Single:
                return new(EventFieldValueKind.Single, BitConverter.SingleToInt32Bits(property.AsSingle), null);
            case EventPropertyKind.Double:
                return new(EventFieldValueKind.Double, BitConverter.DoubleToInt64Bits(property.AsDouble), null);
            case EventPropertyKind.Boolean:
                return new(EventFieldValueKind.Boolean, property.AsBoolean ? 1L : 0L, null);
            case EventPropertyKind.DateTime:
                return new(EventFieldValueKind.DateTime, property.AsDateTime.ToBinary(), null);
            default:
                return property.Reference switch
                {
                    null => new(EventFieldValueKind.Null, 0, null),
                    string text => new(EventFieldValueKind.String, 0, text),
                    Guid guid => new(EventFieldValueKind.Guid, 0, guid),
                    SecurityIdentifier sid => new(EventFieldValueKind.Sid, 0, sid),
                    byte[] bytes => new(EventFieldValueKind.Bytes, 0, bytes),
                    string[] strings => new(EventFieldValueKind.StringArray, 0, strings),
                    Array array => new(EventFieldValueKind.Array, 0, array),
                    var other => new(EventFieldValueKind.String, 0, other.ToString() ?? string.Empty)
                };
        }
    }
}
