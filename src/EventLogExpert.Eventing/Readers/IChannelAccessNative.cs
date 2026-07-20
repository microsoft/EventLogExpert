// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Interop;
using Microsoft.Win32.SafeHandles;

namespace EventLogExpert.Eventing.Readers;

internal interface IChannelAccessNative : IDisposable
{
    bool AccessCheck(
        IntPtr securityDescriptor,
        SafeAccessTokenHandle token,
        uint desiredAccess,
        ref GenericMapping genericMapping,
        Span<byte> privilegeSet,
        ref uint privilegeSetLength,
        out uint grantedAccess,
        out bool accessStatus);

    void FreeSecurityDescriptor(IntPtr securityDescriptor);

    void MapGenericMask(ref uint desiredAccess, ref GenericMapping genericMapping);

    bool PrivilegeCheck(SafeAccessTokenHandle token, string privilegeName, out bool result);

    bool TryConvertSecurityDescriptor(string sddl, out IntPtr securityDescriptor);

    bool TryGetImpersonationToken(out SafeAccessTokenHandle token);
}
