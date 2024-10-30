// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.Models;

[StructLayout(LayoutKind.Sequential)]
internal record struct EvtRpcLogin
{
    [MarshalAs(UnmanagedType.LPWStr)] public string Server;
    [MarshalAs(UnmanagedType.LPWStr)] public string User;
    [MarshalAs(UnmanagedType.LPWStr)] public string Domain;
    [MarshalAs(UnmanagedType.LPWStr)] public string Password;
    public int Flags;
}
