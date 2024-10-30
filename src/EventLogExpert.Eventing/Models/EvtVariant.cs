// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.Models;

[StructLayout(LayoutKind.Explicit)]
internal record struct EvtVariant
{
    [FieldOffset(0)] internal uint UInteger;
    [FieldOffset(0)] internal int Integer;
    [FieldOffset(0)] internal byte UInt8;
    [FieldOffset(0)] internal short Short;
    [FieldOffset(0)] internal ushort UShort;
    [FieldOffset(0)] internal uint Bool;
    [FieldOffset(0)] internal byte ByteVal;
    [FieldOffset(0)] internal byte SByte;
    [FieldOffset(0)] internal ulong ULong;
    [FieldOffset(0)] internal long Long;
    [FieldOffset(0)] internal float Single;
    [FieldOffset(0)] internal double Double;
    [FieldOffset(0)] internal nint StringVal;
    [FieldOffset(0)] internal nint AnsiString;
    [FieldOffset(0)] internal nint SidVal;
    [FieldOffset(0)] internal nint Binary;
    [FieldOffset(0)] internal nint Reference;
    [FieldOffset(0)] internal nint Handle;
    [FieldOffset(0)] internal nint GuidReference;
    [FieldOffset(0)] internal ulong FileTime;
    [FieldOffset(0)] internal nint SystemTime;
    [FieldOffset(0)] internal nint SizeT;
    [FieldOffset(8)] internal uint Count;
    [FieldOffset(12)] internal uint Type;
}
