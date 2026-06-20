// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.Interop;

[StructLayout(LayoutKind.Explicit)]
internal readonly record struct EvtVariant
{
    [FieldOffset(0)] internal readonly int BooleanVal;
    [FieldOffset(0)] internal readonly sbyte SByteVal;
    [FieldOffset(0)] internal readonly short Int16Val;
    [FieldOffset(0)] internal readonly int Int32Val;
    [FieldOffset(0)] internal readonly long Int64Val;
    [FieldOffset(0)] internal readonly byte ByteVal;
    [FieldOffset(0)] internal readonly ushort UInt16Val;
    [FieldOffset(0)] internal readonly uint UInt32Val;
    [FieldOffset(0)] internal readonly ulong UInt64Val;
    [FieldOffset(0)] internal readonly float SingleVal;
    [FieldOffset(0)] internal readonly double DoubleVal;
    [FieldOffset(0)] internal readonly ulong FileTimeVal;
    [FieldOffset(0)] internal readonly nint SysTimeVal;
    [FieldOffset(0)] internal readonly nint GuidVal;
    [FieldOffset(0)] internal readonly nint StringVal;
    [FieldOffset(0)] internal readonly nint AnsiStringVal;
    [FieldOffset(0)] internal readonly nint BinaryVal;
    [FieldOffset(0)] internal readonly nint SidVal;
    [FieldOffset(0)] internal readonly nuint SizeTVal;
    [FieldOffset(0)] internal readonly nint BooleanArr;
    [FieldOffset(0)] internal readonly nint SByteArr;
    [FieldOffset(0)] internal readonly nint Int16Arr;
    [FieldOffset(0)] internal readonly nint Int32Arr;
    [FieldOffset(0)] internal readonly nint Int64Arr;
    [FieldOffset(0)] internal readonly nint ByteArr;
    [FieldOffset(0)] internal readonly nint UInt16Arr;
    [FieldOffset(0)] internal readonly nint UInt32Arr;
    [FieldOffset(0)] internal readonly nint UInt64Arr;
    [FieldOffset(0)] internal readonly nint SingleArr;
    [FieldOffset(0)] internal readonly nint DoubleArr;
    [FieldOffset(0)] internal readonly nint FileTimeArr;
    [FieldOffset(0)] internal readonly nint SysTimeArr;
    [FieldOffset(0)] internal readonly nint GuidArr;
    [FieldOffset(0)] internal readonly nint StringArr;
    [FieldOffset(0)] internal readonly nint AnsiStringArr;
    [FieldOffset(0)] internal readonly nint SidArr;
    [FieldOffset(0)] internal readonly nint SizeTArr;
    [FieldOffset(0)] internal readonly nint EvtHandleVal;
    [FieldOffset(0)] internal readonly nint XmlVal;
    [FieldOffset(0)] internal readonly nint XmlValArr;
    [FieldOffset(8)] internal readonly uint Count;
    [FieldOffset(12)] internal readonly uint Type;
}
