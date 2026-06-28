// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Interop;
using EventLogExpert.Eventing.PublisherMetadata.Offline;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace EventLogExpert.Eventing.Tests.PublisherMetadata.Offline;

public sealed class OfflineRegistryHiveTests
{
    private const int KEY_ALL_ACCESS = 0xF003F;

    [Fact]
    public void TryLoad_FileWithoutHiveSignature_ReturnsNotAHiveWithoutAttemptingRecovery()
    {
        string notAHive = Path.Combine(Path.GetTempPath(), "elx_notahive_" + Guid.NewGuid().ToString("N") + ".dat");
        File.WriteAllBytes(notAHive, "this is not a registry hive"u8.ToArray());

        try
        {
            // No "regf" signature -> rejected before any load/recovery path runs.
            Assert.Equal(OfflineHiveLoadStatus.NotAHive, OfflineRegistryHive.TryLoad(notAHive, logger: null).Status);
        }
        finally
        {
            File.Delete(notAHive);
        }
    }

    [Fact]
    public void TryLoad_NonexistentFile_ReturnsNotAHive()
    {
        string missing = Path.Combine(Path.GetTempPath(), "elx_missing_" + Guid.NewGuid().ToString("N") + ".dat");

        Assert.Equal(OfflineHiveLoadStatus.NotAHive, OfflineRegistryHive.TryLoad(missing, logger: null).Status);
    }

    [Fact]
    public void TryLoad_ReadsValuesAndSubkeysWrittenToAStandaloneHive()
    {
        string hivePath = Path.Combine(Path.GetTempPath(), "elx_test_hive_" + Guid.NewGuid().ToString("N") + ".dat");

        try
        {
            SeedHive(hivePath);

            OfflineHiveLoadResult result = OfflineRegistryHive.TryLoad(hivePath, logger: null);

            Assert.Equal(OfflineHiveLoadStatus.Loaded, result.Status);

            using OfflineRegistryHive? hive = result.Hive;
            Assert.NotNull(hive);

            using RegistryKey? publisher = hive!.Root.OpenSubKey(
                @"Microsoft\Windows\CurrentVersion\WINEVT\Publishers\{11111111-1111-1111-1111-111111111111}");

            Assert.NotNull(publisher);
            Assert.Equal("Test-Provider", publisher!.GetValue(null));
            Assert.Equal(@"%SystemRoot%\System32\test.dll", publisher.GetValue("ResourceFileName"));
        }
        finally
        {
            DeleteHive(hivePath);
        }
    }

    private static void DeleteHive(string hivePath)
    {
        foreach (string suffix in new[] { string.Empty, ".LOG", ".LOG1", ".LOG2" })
        {
            string path = hivePath + suffix;

            if (File.Exists(path)) { File.Delete(path); }
        }
    }

    // Creates a fresh standalone hive file via RegLoadAppKey (which creates the backing file when absent), writes a
    // representative WINEVT publisher subkey, then flushes + unloads so the file is a valid hive the production reader can
    // stage and load. No admin or reg.exe needed - this is the CI-friendly offline-hive fixture.
    private static void SeedHive(string hivePath)
    {
        int result = NativeMethods.RegLoadAppKey(hivePath, out nint handle, KEY_ALL_ACCESS, 0, 0);

        Assert.Equal(0, result);

        using (RegistryKey root = RegistryKey.FromHandle(new SafeRegistryHandle(handle, ownsHandle: true)))
        {
            using RegistryKey publisher = root.CreateSubKey(
                @"Microsoft\Windows\CurrentVersion\WINEVT\Publishers\{11111111-1111-1111-1111-111111111111}");

            publisher.SetValue(null, "Test-Provider");
            publisher.SetValue("ResourceFileName", @"%SystemRoot%\System32\test.dll");
            root.Flush();
        }
    }
}
