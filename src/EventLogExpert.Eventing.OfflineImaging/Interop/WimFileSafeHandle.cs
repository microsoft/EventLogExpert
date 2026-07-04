// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.Win32.SafeHandles;

namespace EventLogExpert.Eventing.OfflineImaging.Interop;

// Closing a WIM file handle also tears down callbacks registered against it.
internal sealed class WimFileSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    // Must be public for the source-generated P/Invoke marshaller to construct the handle for a return value.
    public WimFileSafeHandle() : base(true) { }

    protected override bool ReleaseHandle() => NativeMethods.WIMCloseHandle(handle);
}
