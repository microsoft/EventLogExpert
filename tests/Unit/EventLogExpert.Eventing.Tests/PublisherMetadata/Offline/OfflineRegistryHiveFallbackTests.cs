// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Interop;
using EventLogExpert.Eventing.PublisherMetadata.Offline;
using EventLogExpert.Logging.Abstractions;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace EventLogExpert.Eventing.Tests.PublisherMetadata.Offline;

/// <summary>
///     Drives the dirty-hive recovery state machine deterministically through a fake
///     <see cref="IOfflineHiveNativeApi" />. The synthetic clean hives the other tests use are born clean, so
///     <c>RegLoadAppKey</c> always succeeds on them and the recovery fallback is never exercised - the exact blind spot
///     that let the real-image bug ship. These tests seed a real <c>regf</c> hive (so staging + signature checks run for
///     real) but make the fake app-key load FAIL, then assert each recovery branch.
/// </summary>
public sealed class OfflineRegistryHiveFallbackTests
{
    [Fact]
    public void TryLoad_WhenAppHiveLoadFailsAndPrivilegeUnavailable_ReturnsNeedsElevation()
    {
        using var hive = TempHive.Seeded();
        var nativeApi = new FakeHiveNativeApi
        {
            AppHiveLoadResult = Win32ErrorCodes.ERROR_BADDB,
            RecoveryPrivilegeAvailable = false
        };

        OfflineHiveLoadResult result = OfflineRegistryHive.TryLoad(hive.Path, logger: null, nativeApi);

        Assert.Equal(OfflineHiveLoadStatus.NeedsElevation, result.Status);
        Assert.Equal(0, nativeApi.LoadHiveForRecoveryCalls);
        Assert.Equal(0, nativeApi.UnloadHiveCalls);
    }

    [Fact]
    public void TryLoad_WhenAppHiveLoadFailsAndRecoveryLoadFails_ReturnsRecoveryFailedWithoutLeakingAMount()
    {
        using var hive = TempHive.Seeded();
        var nativeApi = new FakeHiveNativeApi
        {
            AppHiveLoadResult = Win32ErrorCodes.ERROR_BADDB,
            RecoveryPrivilegeAvailable = true,
            RecoveryLoadResult = Win32ErrorCodes.ERROR_REGISTRY_CORRUPT
        };

        OfflineHiveLoadResult result = OfflineRegistryHive.TryLoad(hive.Path, logger: null, nativeApi);

        Assert.Equal(OfflineHiveLoadStatus.RecoveryFailed, result.Status);
        Assert.Equal(1, nativeApi.LoadHiveForRecoveryCalls);
        // RegLoadKey failed, so there is nothing mounted to unload.
        Assert.Equal(0, nativeApi.UnloadHiveCalls);
    }

    [Fact]
    public void TryLoad_WhenAppHiveLoadSucceeds_DoesNotEnterRecovery()
    {
        using var hive = TempHive.Seeded();
        var nativeApi = new FakeHiveNativeApi
        {
            // A real handle so RegistryKey.FromHandle works; the fake just reports success and never recovers.
            AppHiveLoadResult = Win32ErrorCodes.ERROR_SUCCESS,
            AppHiveHandleFactory = () => OpenRealAppHive(hive.Path)
        };

        OfflineHiveLoadResult result = OfflineRegistryHive.TryLoad(hive.Path, logger: null, nativeApi);

        using OfflineRegistryHive? loaded = result.Hive;

        Assert.Equal(OfflineHiveLoadStatus.Loaded, result.Status);
        Assert.Equal(0, nativeApi.TryEnterRecoveryPrivilegeCalls);
        Assert.Equal(0, nativeApi.LoadHiveForRecoveryCalls);
    }

    [Fact]
    public void TryLoad_WhenRecoveryLoadSucceedsButRootCannotOpen_ReturnsRecoveryFailedAndUnloadsTheMount()
    {
        using var hive = TempHive.Seeded();
        var nativeApi = new FakeHiveNativeApi
        {
            AppHiveLoadResult = Win32ErrorCodes.ERROR_BADDB,
            RecoveryPrivilegeAvailable = true,
            RecoveryLoadResult = Win32ErrorCodes.ERROR_SUCCESS,
            MountedRoot = null
        };

        OfflineHiveLoadResult result = OfflineRegistryHive.TryLoad(hive.Path, logger: null, nativeApi);

        Assert.Equal(OfflineHiveLoadStatus.RecoveryFailed, result.Status);
        // The exception-safe partial-failure path must unmount the hive it just mounted, so no HKLM mount leaks.
        Assert.Equal(1, nativeApi.LoadHiveForRecoveryCalls);
        Assert.Equal(1, nativeApi.UnloadHiveCalls);
    }

    private static SafeRegistryHandle OpenRealAppHive(string hivePath)
    {
        int result = NativeMethods.RegLoadAppKey(hivePath, out nint handle, NativeMethods.KEY_READ, 0, 0);
        Assert.Equal(Win32ErrorCodes.ERROR_SUCCESS, result);

        return new SafeRegistryHandle(handle, ownsHandle: true);
    }

    private sealed class FakeHiveNativeApi : IOfflineHiveNativeApi
    {
        public Func<SafeRegistryHandle>? AppHiveHandleFactory { get; init; }

        public int AppHiveLoadResult { get; init; }

        public int LoadHiveForRecoveryCalls { get; private set; }

        public RegistryKey? MountedRoot { get; init; }

        public int RecoveryLoadResult { get; init; }

        public bool RecoveryPrivilegeAvailable { get; init; }

        public int TryEnterRecoveryPrivilegeCalls { get; private set; }

        public int UnloadHiveCalls { get; private set; }

        // The sweep enumerates HKLM through the seam; an empty set keeps these fake-driven tests hermetic from any real
        // ELX_ recovery mount a force-killed elevated run may have left on the host.
        public IReadOnlyList<string> EnumerateHklmSubKeyNames() => Array.Empty<string>();

        public int LoadApplicationHive(string hiveFilePath, out SafeRegistryHandle? root)
        {
            root = AppHiveLoadResult == Win32ErrorCodes.ERROR_SUCCESS ? AppHiveHandleFactory?.Invoke() : null;

            return AppHiveLoadResult;
        }

        public int LoadHiveForRecovery(string mountSubKey, string hiveFilePath)
        {
            LoadHiveForRecoveryCalls++;

            return RecoveryLoadResult;
        }

        public RegistryKey? OpenMountedRoot(string mountSubKey) => MountedRoot;

        public IDisposable? TryEnterRecoveryPrivilege(ITraceLogger? logger)
        {
            TryEnterRecoveryPrivilegeCalls++;

            return RecoveryPrivilegeAvailable ? new NoOpScope() : null;
        }

        public int UnloadHive(string mountSubKey)
        {
            UnloadHiveCalls++;

            return Win32ErrorCodes.ERROR_SUCCESS;
        }

        private sealed class NoOpScope : IDisposable
        {
            public void Dispose() { }
        }
    }

    private sealed class TempHive : IDisposable
    {
        private const int KeyAllAccess = 0xF003F;

        private TempHive(string path) => Path = path;

        public string Path { get; }

        public static TempHive Seeded()
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "elx_fallback_" + Guid.NewGuid().ToString("N") + ".dat");

            int result = NativeMethods.RegLoadAppKey(path, out nint handle, KeyAllAccess, 0, 0);
            Assert.Equal(0, result);

            using (RegistryKey root = RegistryKey.FromHandle(new SafeRegistryHandle(handle, ownsHandle: true)))
            {
                using RegistryKey key = root.CreateSubKey(@"Microsoft\Windows\CurrentVersion\WINEVT\Publishers");
                key.SetValue("seeded", 1);
                root.Flush();
            }

            return new TempHive(path);
        }

        public void Dispose()
        {
            foreach (string suffix in new[] { string.Empty, ".LOG", ".LOG1", ".LOG2" })
            {
                if (File.Exists(Path + suffix)) { File.Delete(Path + suffix); }
            }
        }
    }
}
