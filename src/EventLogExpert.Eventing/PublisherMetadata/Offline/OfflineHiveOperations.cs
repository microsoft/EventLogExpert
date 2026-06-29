// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Interop;
using EventLogExpert.Logging.Abstractions;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace EventLogExpert.Eventing.PublisherMetadata.Offline;

/// <summary>The production <see cref="IOfflineHiveOperations" />, calling the real Win32 registry APIs.</summary>
internal sealed class OfflineHiveOperations : IOfflineHiveOperations
{
    internal static OfflineHiveOperations Instance { get; } = new();

    public IReadOnlyList<string> EnumerateHklmSubKeyNames()
    {
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);

        return hklm.GetSubKeyNames();
    }

    public int LoadApplicationHive(string hiveFilePath, out SafeRegistryHandle? root)
    {
        int result = NativeMethods.RegLoadAppKey(hiveFilePath, out nint handle, NativeMethods.KEY_READ, 0, 0);
        root = result == Win32ErrorCodes.ERROR_SUCCESS ? new SafeRegistryHandle(handle, ownsHandle: true) : null;

        return result;
    }

    public int LoadHiveForRecovery(string mountSubKey, string hiveFilePath) =>
        NativeMethods.RegLoadKey(NativeMethods.HKEY_LOCAL_MACHINE, mountSubKey, hiveFilePath);

    public RegistryKey? OpenMountedRoot(string mountSubKey)
    {
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);

        return hklm.OpenSubKey(mountSubKey, writable: false);
    }

    public IDisposable? TryEnterRecoveryPrivilege(ITraceLogger? logger) => BackupRestorePrivilegeScope.TryAcquire(logger);

    public int UnloadHive(string mountSubKey) => NativeMethods.RegUnLoadKey(NativeMethods.HKEY_LOCAL_MACHINE, mountSubKey);
}
