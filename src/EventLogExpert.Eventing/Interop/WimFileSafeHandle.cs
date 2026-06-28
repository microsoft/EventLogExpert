// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.Win32.SafeHandles;

namespace EventLogExpert.Eventing.Interop;

/// <summary>
///     A WIM file handle returned by <see cref="NativeMethods.WIMCreateFile" />, released with <c>WIMCloseHandle</c>.
///     Closing the WIM file handle also tears down any message callbacks registered against it.
/// </summary>
internal sealed class WimFileSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    // Must be public for the source-generated P/Invoke marshaller to construct the handle for a return value.
    public WimFileSafeHandle() : base(true) { }

    protected override bool ReleaseHandle() => NativeMethods.WIMCloseHandle(handle);
}
