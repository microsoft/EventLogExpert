// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.Models;

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct EvtRpcLogin
{
    [MarshalAs(UnmanagedType.LPWStr)] public readonly string Server;
    [MarshalAs(UnmanagedType.LPWStr)] public readonly string User;
    [MarshalAs(UnmanagedType.LPWStr)] public readonly string Domain;
    [MarshalAs(UnmanagedType.LPWStr)] public readonly string Password;
    public readonly int Flags;
}
