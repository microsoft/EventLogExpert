// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Interop;
using Microsoft.Win32.SafeHandles;
using System.Buffers;
using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.Readers;

internal sealed class Win32ChannelAccessNative : IChannelAccessNative
{
    private const uint PrivilegeSetAllNecessary = 1;
    private const uint SecurityDescriptorRevision = 1;
    private const uint SePrivilegeEnabled = 0x2;
    private const uint TokenDuplicate = 0x2;
    private const uint TokenImpersonate = 0x4;
    private const uint TokenQuery = 0x8;

    private readonly Lock _tokenLock = new();

    private SafeAccessTokenHandle? _impersonationToken;

    public unsafe bool AccessCheck(
        IntPtr securityDescriptor,
        SafeAccessTokenHandle token,
        uint desiredAccess,
        ref GenericMapping genericMapping,
        Span<byte> privilegeSet,
        ref uint privilegeSetLength,
        out uint grantedAccess,
        out bool accessStatus)
    {
        fixed (byte* privilegeSetPointer = privilegeSet)
        {
            if (NativeMethods.AccessCheck(
                    securityDescriptor,
                    token,
                    desiredAccess,
                    ref genericMapping,
                    (IntPtr)privilegeSetPointer,
                    ref privilegeSetLength,
                    out grantedAccess,
                    out accessStatus))
            {
                return true;
            }

            if (Marshal.GetLastWin32Error() != Win32ErrorCodes.ERROR_INSUFFICIENT_BUFFER)
            {
                return false;
            }
        }

        byte[] rentedPrivilegeSet = ArrayPool<byte>.Shared.Rent((int)privilegeSetLength);

        try
        {
            fixed (byte* resizedPrivilegeSetPointer = rentedPrivilegeSet)
            {
                return NativeMethods.AccessCheck(
                    securityDescriptor,
                    token,
                    desiredAccess,
                    ref genericMapping,
                    (IntPtr)resizedPrivilegeSetPointer,
                    ref privilegeSetLength,
                    out grantedAccess,
                    out accessStatus);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rentedPrivilegeSet);
        }
    }

    public void Dispose()
    {
        lock (_tokenLock)
        {
            _impersonationToken?.Dispose();
            _impersonationToken = null;
        }
    }

    public void FreeSecurityDescriptor(IntPtr securityDescriptor)
    {
        if (securityDescriptor != IntPtr.Zero)
        {
            _ = NativeMethods.LocalFree(securityDescriptor);
        }
    }

    public void MapGenericMask(ref uint desiredAccess, ref GenericMapping genericMapping) =>
        NativeMethods.MapGenericMask(ref desiredAccess, ref genericMapping);

    public bool PrivilegeCheck(SafeAccessTokenHandle token, string privilegeName, out bool result)
    {
        result = false;

        if (!NativeMethods.LookupPrivilegeValue(null, privilegeName, out var luid))
        {
            return false;
        }

        var privilegeSet = new PrivilegeSet
        {
            PrivilegeCount = 1,
            Control = PrivilegeSetAllNecessary,
            Privilege = new LuidAndAttributes
            {
                Luid = luid,
                Attributes = SePrivilegeEnabled
            }
        };

        return NativeMethods.PrivilegeCheck(token, ref privilegeSet, out result);
    }

    public bool TryConvertSecurityDescriptor(string sddl, out IntPtr securityDescriptor) =>
        NativeMethods.ConvertStringSecurityDescriptorToSecurityDescriptor(
            sddl,
            SecurityDescriptorRevision,
            out securityDescriptor,
            out _);

    public bool TryGetImpersonationToken(out SafeAccessTokenHandle token)
    {
        lock (_tokenLock)
        {
            if (_impersonationToken is { IsClosed: false, IsInvalid: false })
            {
                token = _impersonationToken;
                return true;
            }

            if (!NativeMethods.OpenProcessToken(
                    NativeMethods.GetCurrentProcess(),
                    TokenDuplicate | TokenQuery,
                    out var processToken))
            {
                token = SafeAccessTokenHandle.InvalidHandle;
                return false;
            }

            using (processToken)
            {
                if (!NativeMethods.DuplicateTokenEx(
                        processToken,
                        TokenQuery | TokenImpersonate,
                        IntPtr.Zero,
                        SecurityImpersonationLevel.SecurityImpersonation,
                        TokenType.TokenImpersonation,
                        out var impersonationToken))
                {
                    token = SafeAccessTokenHandle.InvalidHandle;
                    return false;
                }

                _impersonationToken = impersonationToken;
                token = _impersonationToken;

                return true;
            }
        }
    }
}
