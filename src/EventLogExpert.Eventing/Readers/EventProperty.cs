// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Security.Principal;

namespace EventLogExpert.Eventing.Readers;

internal enum EventPropertyKind : byte
{
    SByte,
    Byte,
    Int16,
    UInt16,
    Int32,
    UInt32,
    Int64,
    UInt64,
    Single,
    Double,
    Boolean,
    DateTime,
    SizeT,
    Reference
}

/// <summary>
///     A single rendered event-data property stored without boxing value types: numeric / bool / DateTime kinds pack
///     into a 64-bit field tagged by a shared per-kind sentinel, while reference shapes (string, byte[], Guid, SID,
///     arrays, handle) live in the object slot.
/// </summary>
internal readonly struct EventProperty : IEquatable<EventProperty>
{
    private static readonly NumericKind[] s_numericKinds =
    [
        new(EventPropertyKind.SByte),
        new(EventPropertyKind.Byte),
        new(EventPropertyKind.Int16),
        new(EventPropertyKind.UInt16),
        new(EventPropertyKind.Int32),
        new(EventPropertyKind.UInt32),
        new(EventPropertyKind.Int64),
        new(EventPropertyKind.UInt64),
        new(EventPropertyKind.Single),
        new(EventPropertyKind.Double),
        new(EventPropertyKind.Boolean),
        new(EventPropertyKind.DateTime),
        new(EventPropertyKind.SizeT)
    ];

    private readonly object? _tagOrRef;
    private readonly long _bits;

    private EventProperty(EventPropertyKind kind, long bits)
    {
        _tagOrRef = s_numericKinds[(int)kind];
        _bits = bits;
    }

    private EventProperty(object? reference)
    {
        _tagOrRef = reference;
        _bits = 0;
    }

    public EventPropertyKind Kind => _tagOrRef is NumericKind numericKind ? numericKind.Kind : EventPropertyKind.Reference;

    internal object? Reference => _tagOrRef is NumericKind ? null : _tagOrRef;

    internal long PackedBits => _bits;

    internal bool AsBoolean => _bits != 0;

    internal sbyte AsSByte => (sbyte)_bits;

    internal byte AsByte => (byte)_bits;

    internal short AsInt16 => (short)_bits;

    internal ushort AsUInt16 => (ushort)_bits;

    internal int AsInt32 => (int)_bits;

    internal uint AsUInt32 => (uint)_bits;

    internal long AsInt64 => _bits;

    internal ulong AsUInt64 => (ulong)_bits;

    internal float AsSingle => BitConverter.Int32BitsToSingle((int)_bits);

    internal double AsDouble => BitConverter.Int64BitsToDouble(_bits);

    internal nuint AsSizeT => (nuint)(ulong)_bits;

    internal DateTime AsDateTime => DateTime.FromBinary(_bits);

    public static implicit operator EventProperty(sbyte value) => new(EventPropertyKind.SByte, value);

    public static implicit operator EventProperty(byte value) => new(EventPropertyKind.Byte, value);

    public static implicit operator EventProperty(short value) => new(EventPropertyKind.Int16, value);

    public static implicit operator EventProperty(ushort value) => new(EventPropertyKind.UInt16, value);

    public static implicit operator EventProperty(int value) => new(EventPropertyKind.Int32, value);

    public static implicit operator EventProperty(uint value) => new(EventPropertyKind.UInt32, value);

    public static implicit operator EventProperty(long value) => new(EventPropertyKind.Int64, value);

    public static implicit operator EventProperty(ulong value) => new(EventPropertyKind.UInt64, unchecked((long)value));

    public static implicit operator EventProperty(float value) => new(EventPropertyKind.Single, BitConverter.SingleToInt32Bits(value));

    public static implicit operator EventProperty(double value) => new(EventPropertyKind.Double, BitConverter.DoubleToInt64Bits(value));

    public static implicit operator EventProperty(bool value) => new(EventPropertyKind.Boolean, value ? 1L : 0L);

    public static implicit operator EventProperty(DateTime value) => new(EventPropertyKind.DateTime, value.ToBinary());

    public static implicit operator EventProperty(nuint value) => new(EventPropertyKind.SizeT, unchecked((long)value));

    public static implicit operator EventProperty(string? value) => new(value);

    public static implicit operator EventProperty(byte[]? value) => new(value);

    public static implicit operator EventProperty(string[]? value) => new(value);

    public static implicit operator EventProperty(Guid value) => new((object)value);

    public static implicit operator EventProperty(SecurityIdentifier? value) => new(value);

    public static bool operator ==(EventProperty left, EventProperty right) => left.Equals(right);

    public static bool operator !=(EventProperty left, EventProperty right) => !left.Equals(right);

    public bool Equals(EventProperty other) => Equals(_tagOrRef, other._tagOrRef) && _bits == other._bits;

    public override bool Equals(object? obj) => obj is EventProperty other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_tagOrRef, _bits);

    /// <summary>
    ///     Extracts the native-width unsigned value for valueMap / bitMap decoding, matching the manifest's integral
    ///     types exactly: returns false for float, double, SizeT, and every reference shape.
    /// </summary>
    internal bool TryGetUnsignedBits(out ulong bits)
    {
        switch (Kind)
        {
            case EventPropertyKind.Byte: bits = (byte)_bits; return true;
            case EventPropertyKind.SByte: bits = (byte)(sbyte)_bits; return true;
            case EventPropertyKind.UInt16: bits = (ushort)_bits; return true;
            case EventPropertyKind.Int16: bits = (ushort)(short)_bits; return true;
            case EventPropertyKind.UInt32: bits = (uint)_bits; return true;
            case EventPropertyKind.Int32: bits = (uint)(int)_bits; return true;
            case EventPropertyKind.UInt64: bits = (ulong)_bits; return true;
            case EventPropertyKind.Int64: bits = (ulong)_bits; return true;
            default: bits = 0; return false;
        }
    }

    internal static EventProperty FromReference(object? reference) => new(reference);

    private sealed class NumericKind(EventPropertyKind kind)
    {
        internal EventPropertyKind Kind { get; } = kind;
    }
}
