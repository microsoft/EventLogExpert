// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using Microsoft.Win32.SafeHandles;

namespace EventLogExpert.Eventing.Models;

internal sealed partial class EventLogHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal EventLogHandle(IntPtr handle) : base(true)
    {
        SetHandle(handle);
    }

    // Must be public for P/Invoke to work
    public EventLogHandle() : base(true) { }

    internal static EventLogHandle Zero => new();

    protected override bool ReleaseHandle()
    {
        EventMethods.EvtClose(handle);
        handle = IntPtr.Zero;

        return true;
    }
}
