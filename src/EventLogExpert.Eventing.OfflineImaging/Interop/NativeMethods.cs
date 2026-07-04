// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Runtime.InteropServices;

// ReSharper disable InconsistentNaming
namespace EventLogExpert.Eventing.OfflineImaging.Interop;

internal static partial class NativeMethods
{
    private const string Kernel32Api = "kernel32.dll";

    [LibraryImport(Kernel32Api, EntryPoint = "CloseHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(IntPtr handle);
}
