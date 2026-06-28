// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.Win32.SafeHandles;

namespace EventLogExpert.Eventing.Interop;

/// <summary>
///     A loaded WIM image handle returned by <see cref="NativeMethods.WIMLoadImage" />, released with
///     <c>WIMCloseHandle</c>. It must be closed before its parent <see cref="WimFileSafeHandle" />.
/// </summary>
internal sealed class WimImageSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    // Must be public for the source-generated P/Invoke marshaller to construct the handle for a return value.
    public WimImageSafeHandle() : base(true) { }

    protected override bool ReleaseHandle() => NativeMethods.WIMCloseHandle(handle);
}
