// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.Models;

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct SystemTime
{
    [MarshalAs(UnmanagedType.U2)] public readonly short Year;
    [MarshalAs(UnmanagedType.U2)] public readonly short Month;
    [MarshalAs(UnmanagedType.U2)] public readonly short DayOfWeek;
    [MarshalAs(UnmanagedType.U2)] public readonly short Day;
    [MarshalAs(UnmanagedType.U2)] public readonly short Hour;
    [MarshalAs(UnmanagedType.U2)] public readonly short Minute;
    [MarshalAs(UnmanagedType.U2)] public readonly short Second;
    [MarshalAs(UnmanagedType.U2)] public readonly short Milliseconds;
}
