// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using Microsoft.Win32.SafeHandles;

namespace EventLogExpert.Eventing.Models;

internal sealed partial class EventLogHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    // Must be public for P/Invoke to work
    public EventLogHandle() : base(true) { }

    internal EventLogHandle(IntPtr handle) : base(true)
    {
        SetHandle(handle);
    }

    internal EventLogHandle(IntPtr handle, bool ownsHandle) : base(ownsHandle)
    {
        SetHandle(handle);
    }

    internal static EventLogHandle Zero => new();

    protected override bool ReleaseHandle()
    {
        EventMethods.EvtClose(handle);
        handle = IntPtr.Zero;

        return true;
    }
}
