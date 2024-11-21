// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.Models;

[StructLayout(LayoutKind.Explicit)]
internal readonly record struct EvtVariant
{
    [FieldOffset(0)] internal readonly uint UInteger;
    [FieldOffset(0)] internal readonly int Integer;
    [FieldOffset(0)] internal readonly byte UInt8;
    [FieldOffset(0)] internal readonly short Short;
    [FieldOffset(0)] internal readonly ushort UShort;
    [FieldOffset(0)] internal readonly uint Bool;
    [FieldOffset(0)] internal readonly byte ByteVal;
    [FieldOffset(0)] internal readonly byte SByte;
    [FieldOffset(0)] internal readonly ulong ULong;
    [FieldOffset(0)] internal readonly long Long;
    [FieldOffset(0)] internal readonly float Single;
    [FieldOffset(0)] internal readonly double Double;
    [FieldOffset(0)] internal readonly nint StringVal;
    [FieldOffset(0)] internal readonly nint AnsiString;
    [FieldOffset(0)] internal readonly nint SidVal;
    [FieldOffset(0)] internal readonly nint Binary;
    [FieldOffset(0)] internal readonly nint Reference;
    [FieldOffset(0)] internal readonly nint Handle;
    [FieldOffset(0)] internal readonly nint GuidReference;
    [FieldOffset(0)] internal readonly ulong FileTime;
    [FieldOffset(0)] internal readonly nint SystemTime;
    [FieldOffset(0)] internal readonly nint SizeT;
    [FieldOffset(8)] internal readonly uint Count;
    [FieldOffset(12)] internal readonly uint Type;
}
