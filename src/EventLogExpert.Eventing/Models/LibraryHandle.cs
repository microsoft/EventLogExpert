// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using Microsoft.Win32.SafeHandles;

namespace EventLogExpert.Eventing.Models;

internal sealed partial class LibraryHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    // Must be public for P/Invoke to work
    public LibraryHandle() : base(true) { }

    internal LibraryHandle(IntPtr handle) : base(true)
    {
        SetHandle(handle);
    }

    internal LibraryHandle(IntPtr handle, bool ownsHandle) : base(ownsHandle)
    {
        SetHandle(handle);
    }

    internal static LibraryHandle Zero => new();

    protected override bool ReleaseHandle()
    {
        NativeMethods.FreeLibrary(handle);
        handle = IntPtr.Zero;

        return true;
    }
}
