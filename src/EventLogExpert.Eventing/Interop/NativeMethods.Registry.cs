// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.Interop;

internal static partial class NativeMethods
{
    /// <summary>Read access mask (STANDARD_RIGHTS_READ | KEY_QUERY_VALUE | KEY_ENUMERATE_SUB_KEYS | KEY_NOTIFY).</summary>
    internal const int KEY_READ = 0x20019;
    internal const int SE_PRIVILEGE_ENABLED = 0x00000002;
    internal const int TOKEN_ADJUST_PRIVILEGES = 0x00000020;
    internal const int TOKEN_QUERY = 0x00000008;

    private const string Advapi32Api = "advapi32.dll";

    /// <summary>Predefined handle for HKEY_LOCAL_MACHINE, under which recovery-loaded image hives are mounted.</summary>
    internal static readonly IntPtr HKEY_LOCAL_MACHINE = unchecked((int)0x80000002);

    /// <summary>
    ///     Enables or disables the single privilege in <paramref name="newState" /> on <paramref name="tokenHandle" />,
    ///     writing the prior state to <paramref name="previousState" /> so the caller can restore it exactly. The BOOL return
    ///     is <see langword="true" /> even when the privilege is not held by the token; callers MUST also check
    ///     <c>GetLastError() == ERROR_SUCCESS</c> (a not-held privilege reports <c>ERROR_NOT_ALL_ASSIGNED</c>).
    /// </summary>
    [LibraryImport(Advapi32Api, EntryPoint = "AdjustTokenPrivileges", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AdjustTokenPrivileges(
        IntPtr tokenHandle,
        [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges,
        ref TOKEN_PRIVILEGES newState,
        int bufferLength,
        ref TOKEN_PRIVILEGES previousState,
        out int returnLength);

    [LibraryImport(Kernel32Api, EntryPoint = "CloseHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(IntPtr handle);

    [LibraryImport(Kernel32Api, EntryPoint = "GetCurrentProcess")]
    internal static partial IntPtr GetCurrentProcess();

    [LibraryImport(Advapi32Api, EntryPoint = "LookupPrivilegeValueW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool LookupPrivilegeValue(string? lpSystemName, string lpName, out long lpLuid);

    [LibraryImport(Advapi32Api, EntryPoint = "OpenProcessToken", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool OpenProcessToken(IntPtr processHandle, int desiredAccess, out IntPtr tokenHandle);

    /// <summary>
    ///     Loads the registry hive in <paramref name="lpFile" /> into a private, process-local application subtree and
    ///     returns the root key handle. Unlike <c>RegLoadKey</c> it needs no backup/restore privilege (so it runs unelevated),
    ///     and the hive auto-unloads once every returned key is closed. The handle is returned as a raw <see cref="IntPtr" />
    ///     because the source-generated marshaller cannot construct a <c>SafeRegistryHandle</c> for an <c>out</c> parameter;
    ///     the caller wraps it. Returns a non-zero Win32 error code on failure (it does not set last error). It cannot replay
    ///     the dual-log sidecars a dirty image hive carries - that recovery requires <see cref="RegLoadKey" />.
    /// </summary>
    [LibraryImport(Advapi32Api, EntryPoint = "RegLoadAppKeyW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int RegLoadAppKey(string lpFile, out IntPtr phkResult, int samDesired, int dwOptions, int reserved);

    /// <summary>
    ///     Mounts the hive in <paramref name="lpFile" /> under <paramref name="hKey" />\<paramref name="lpSubKey" />,
    ///     performing log recovery (replaying the dual <c>.LOG1</c>/<c>.LOG2</c> sidecars a dirty image hive carries) that
    ///     <see cref="RegLoadAppKey" /> cannot. Requires <c>SeBackupPrivilege</c> + <c>SeRestorePrivilege</c>; returns a
    ///     non-zero Win32 error code on failure. The named subtree persists until <see cref="RegUnLoadKey" />.
    /// </summary>
    [LibraryImport(Advapi32Api, EntryPoint = "RegLoadKeyW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int RegLoadKey(IntPtr hKey, string lpSubKey, string lpFile);

    /// <summary>
    ///     Unmounts the hive previously mounted at <paramref name="hKey" />\<paramref name="lpSubKey" /> by
    ///     <see cref="RegLoadKey" />. Requires <c>SeRestorePrivilege</c>; fails while any key under the subtree is still open.
    ///     Returns a non-zero Win32 error code on failure.
    /// </summary>
    [LibraryImport(Advapi32Api, EntryPoint = "RegUnLoadKeyW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int RegUnLoadKey(IntPtr hKey, string lpSubKey);

    // TOKEN_PRIVILEGES with exactly one LUID_AND_ATTRIBUTES. Pack=4 is REQUIRED: the native layout is
    // DWORD PrivilegeCount; LUID(8 bytes, 4-aligned); DWORD Attributes. Default 8-byte packing would 8-align the
    // long Luid (offset 8 instead of 4), so AdjustTokenPrivileges reads a misaligned LUID and silently enables nothing
    // (returns TRUE with ERROR_NOT_ALL_ASSIGNED) - the exact wrong-Pack trap the lease unit test guards against.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct TOKEN_PRIVILEGES
    {
        internal int PrivilegeCount;
        internal long Luid;
        internal int Attributes;
    }
}
