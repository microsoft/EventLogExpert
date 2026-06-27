// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.Interop;

internal static partial class NativeMethods
{
    /// <summary>Read access mask (STANDARD_RIGHTS_READ | KEY_QUERY_VALUE | KEY_ENUMERATE_SUB_KEYS | KEY_NOTIFY).</summary>
    internal const int KEY_READ = 0x20019;

    private const string Advapi32Api = "advapi32.dll";

    /// <summary>
    ///     Loads the registry hive in <paramref name="lpFile" /> into a private, process-local application subtree and
    ///     returns the root key handle. Unlike <c>RegLoadKey</c> it needs no backup/restore privilege (so it runs unelevated),
    ///     and the hive auto-unloads once every returned key is closed. The handle is returned as a raw <see cref="IntPtr" />
    ///     because the source-generated marshaller cannot construct a <c>SafeRegistryHandle</c> for an <c>out</c> parameter;
    ///     the caller wraps it. Returns a non-zero Win32 error code on failure (it does not set last error).
    /// </summary>
    [LibraryImport(Advapi32Api, EntryPoint = "RegLoadAppKeyW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int RegLoadAppKey(string lpFile, out IntPtr phkResult, int samDesired, int dwOptions, int reserved);
}
