// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.Win32.SafeHandles;

namespace EventLogExpert.Eventing.OfflineImaging.Interop;

// Close image handles before the parent WIM file handle.
internal sealed class WimImageSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    // Must be public for the source-generated P/Invoke marshaller to construct the handle for a return value.
    public WimImageSafeHandle() : base(true) { }

    protected override bool ReleaseHandle() => NativeMethods.WIMCloseHandle(handle);
}
