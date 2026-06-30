// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.Win32.SafeHandles;

namespace EventLogExpert.Eventing.Interop;

// Closing a non-permanent virtual disk handle auto-detaches the ISO.
internal sealed class VirtualDiskSafeHandle() : SafeHandleZeroOrMinusOneIsInvalid(true)
{
    // Must be public for the source-generated P/Invoke marshaller to construct the handle for a return value.

    protected override bool ReleaseHandle() => NativeMethods.CloseHandle(handle);
}
