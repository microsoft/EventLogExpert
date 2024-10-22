// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.Models;

[StructLayout(LayoutKind.Explicit)]
internal struct EvtVariant
{
    [FieldOffset(0)]
    public uint UInteger;

    [FieldOffset(0)]
    public int Integer;

    [FieldOffset(0)]
    public byte UInt8;

    [FieldOffset(0)]
    public short Short;

    [FieldOffset(0)]
    public ushort UShort;

    [FieldOffset(0)]
    public uint Bool;

    [FieldOffset(0)]
    public byte ByteVal;

    [FieldOffset(0)]
    public byte SByte;

    [FieldOffset(0)]
    public ulong ULong;

    [FieldOffset(0)]
    public long Long;

    [FieldOffset(0)]
    public float Single;

    [FieldOffset(0)]
    public double Double;

    [FieldOffset(0)]
    public nint StringVal;

    [FieldOffset(0)]
    public nint AnsiString;

    [FieldOffset(0)]
    public nint SidVal;

    [FieldOffset(0)]
    public nint Binary;

    [FieldOffset(0)]
    public nint Reference;

    [FieldOffset(0)]
    public nint Handle;

    [FieldOffset(0)]
    public nint GuidReference;

    [FieldOffset(0)]
    public ulong FileTime;

    [FieldOffset(0)]
    public nint SystemTime;

    [FieldOffset(0)]
    public nint SizeT;

    [FieldOffset(8)]
    public uint Count;

    [FieldOffset(12)]
    public uint Type;
}
