// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.Win32.SafeHandles;

namespace EventLogExpert.Eventing.Interop;

/// <summary>
///     An open virtual-disk handle from <see cref="NativeMethods.OpenVirtualDisk" />. Closing it auto-detaches the
///     ISO because the attach is non-permanent, so a crashed run never leaves the image mounted.
/// </summary>
internal sealed class VirtualDiskSafeHandle() : SafeHandleZeroOrMinusOneIsInvalid(true)
{
    // Must be public for the source-generated P/Invoke marshaller to construct the handle for a return value.

    protected override bool ReleaseHandle() => NativeMethods.CloseHandle(handle);
}
