// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.Models;

[StructLayout(LayoutKind.Sequential)]
internal record struct SystemTime
{
    [MarshalAs(UnmanagedType.U2)] public short Year;
    [MarshalAs(UnmanagedType.U2)] public short Month;
    [MarshalAs(UnmanagedType.U2)] public short DayOfWeek;
    [MarshalAs(UnmanagedType.U2)] public short Day;
    [MarshalAs(UnmanagedType.U2)] public short Hour;
    [MarshalAs(UnmanagedType.U2)] public short Minute;
    [MarshalAs(UnmanagedType.U2)] public short Second;
    [MarshalAs(UnmanagedType.U2)] public short Milliseconds;
}
