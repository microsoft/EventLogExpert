// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.Win32.SafeHandles;

namespace EventLogExpert.Eventing.Interop;

internal sealed partial class EvtHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    // Must be public for P/Invoke to work
    public EvtHandle() : base(true) { }

    internal EvtHandle(IntPtr handle) : base(true)
    {
        SetHandle(handle);
    }

    internal EvtHandle(IntPtr handle, bool ownsHandle) : base(ownsHandle)
    {
        SetHandle(handle);
    }

    internal static EvtHandle Zero => new();

    protected override bool ReleaseHandle()
    {
        NativeMethods.EvtClose(handle);
        handle = IntPtr.Zero;

        return true;
    }
}
