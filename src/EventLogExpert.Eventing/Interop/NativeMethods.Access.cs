// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.Interop;

internal static partial class NativeMethods
{
    private const string Advapi32Api = "advapi32.dll";

    [LibraryImport(Advapi32Api, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AccessCheck(
        IntPtr securityDescriptor,
        SafeAccessTokenHandle clientToken,
        uint desiredAccess,
        ref GenericMapping genericMapping,
        IntPtr privilegeSet,
        ref uint privilegeSetLength,
        out uint grantedAccess,
        [MarshalAs(UnmanagedType.Bool)] out bool accessStatus);

    [LibraryImport(Advapi32Api, EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ConvertStringSecurityDescriptorToSecurityDescriptor(
        string stringSecurityDescriptor,
        uint stringSecurityDescriptorRevision,
        out IntPtr securityDescriptor,
        out uint securityDescriptorSize);

    [LibraryImport(Advapi32Api, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DuplicateTokenEx(
        SafeAccessTokenHandle existingToken,
        uint desiredAccess,
        IntPtr tokenAttributes,
        SecurityImpersonationLevel impersonationLevel,
        TokenType tokenType,
        out SafeAccessTokenHandle newToken);

    [LibraryImport(Kernel32Api, SetLastError = true)]
    internal static partial IntPtr GetCurrentProcess();

    [LibraryImport(Kernel32Api, SetLastError = true)]
    internal static partial IntPtr LocalFree(IntPtr memory);

    [LibraryImport(Advapi32Api, EntryPoint = "LookupPrivilegeValueW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool LookupPrivilegeValue(
        string? systemName,
        string name,
        out Luid luid);

    [LibraryImport(Advapi32Api)]
    internal static partial void MapGenericMask(ref uint accessMask, ref GenericMapping genericMapping);

    [LibraryImport(Advapi32Api, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool OpenProcessToken(
        IntPtr processHandle,
        uint desiredAccess,
        out SafeAccessTokenHandle tokenHandle);

    [LibraryImport(Advapi32Api, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PrivilegeCheck(
        SafeAccessTokenHandle clientToken,
        ref PrivilegeSet requiredPrivileges,
        [MarshalAs(UnmanagedType.Bool)] out bool result);
}

[StructLayout(LayoutKind.Sequential)]
internal struct GenericMapping
{
    internal uint GenericRead;
    internal uint GenericWrite;
    internal uint GenericExecute;
    internal uint GenericAll;
}

internal enum SecurityImpersonationLevel
{
    SecurityAnonymous = 0,
    SecurityIdentification = 1,
    SecurityImpersonation = 2,
    SecurityDelegation = 3
}

internal enum TokenType
{
    TokenPrimary = 1,
    TokenImpersonation = 2
}

[StructLayout(LayoutKind.Sequential)]
internal struct Luid
{
    internal uint LowPart;
    internal int HighPart;
}

[StructLayout(LayoutKind.Sequential)]
internal struct LuidAndAttributes
{
    internal Luid Luid;
    internal uint Attributes;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PrivilegeSet
{
    internal uint PrivilegeCount;
    internal uint Control;
    internal LuidAndAttributes Privilege;
}
