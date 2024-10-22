// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Runtime.CompilerServices;

namespace EventLogExpert.Eventing.Helpers;

internal static class Converter
{
    // Implementation of HRESULT_FROM_WIN32 macro
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int HResultFromWin32(int error)
    {
        if ((error & 0x80000000) == 0x80000000)
        {
            return error;
        }

        return (error & 0x0000FFFF) | unchecked((int)0x80070000);
    }
}
