// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.PublisherMetadata.Offline.Containment;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.Tests.PublisherMetadata.Offline;

/// <summary>
///     Builds a throwaway on-disk Windows-image scaffold (
///     <c>&lt;temp&gt;\Windows\System32\config\{SOFTWARE,SYSTEM}</c>) for offline-extraction unit tests, with no admin and
///     no real Windows image. Each hive is either an empty placeholder (enough for path-only components such as the mapper
///     and root-guard) or a real standalone hive seeded via <see cref="RegLoadAppKey" /> (for the catalog and
///     legacy-resolver readers). The scaffold's root directory is the image root, so a mapped host path such as
///     <c>C:\Windows\System32\foo.dll</c> correctly lands under the scaffold - never the host - even though the scaffold
///     itself lives on the host drive.
/// </summary>
internal sealed partial class OfflineTestImage : IDisposable
{
    private const int KeyAllAccess = 0xF003F;
    private const int KeyRead = 0x20019; // STANDARD_RIGHTS_READ | KEY_QUERY_VALUE | KEY_ENUMERATE_SUB_KEYS | KEY_NOTIFY.

    private OfflineTestImage(string rootDirectory, OfflineImageRoot imageRoot)
    {
        RootDirectory = rootDirectory;
        ImageRoot = imageRoot;
    }

    public OfflineImageRoot ImageRoot { get; }

    /// <summary>The image root directory (the directory that contains <c>Windows</c>).</summary>
    public string RootDirectory { get; }

    public static OfflineTestImage Create(
        Action<RegistryKey>? seedSoftware = null,
        Action<RegistryKey>? seedSystem = null)
    {
        string rootDirectory = Path.Combine(Path.GetTempPath(), "elx_img_" + Guid.NewGuid().ToString("N"));
        string configDirectory = Path.Combine(rootDirectory, "Windows", "System32", "config");
        Directory.CreateDirectory(configDirectory);

        SeedOrTouchHive(Path.Combine(configDirectory, "SOFTWARE"), seedSoftware);
        SeedOrTouchHive(Path.Combine(configDirectory, "SYSTEM"), seedSystem);

        OfflineImageRoot imageRoot = OfflineImageRoot.TryCreate(rootDirectory, logger: null)
            ?? throw new InvalidOperationException($"Failed to create OfflineImageRoot for scaffold {rootDirectory}.");

        return new OfflineTestImage(rootDirectory, imageRoot);
    }

    /// <summary>
    ///     Enumerates a key's immediate subkey names through the LIVE registry (read-only) so a test can assert
    ///     <see cref="OfflineHiveFile.GetSubKeyNames" /> returns the identical physical leaf order. Same load/unload ordering
    ///     caveat as <see cref="ReadValueViaLiveRegistry" />.
    /// </summary>
    public static IReadOnlyList<string> ReadSubKeyNamesViaLiveRegistry(string hivePath, string subKeyPath)
    {
        int result = RegLoadAppKey(hivePath, out nint handle, KeyRead, 0, 0);

        if (result != 0)
        {
            throw new InvalidOperationException($"RegLoadAppKey (read) failed for {hivePath} (error {result}).");
        }

        using RegistryKey root = RegistryKey.FromHandle(new SafeRegistryHandle(handle, ownsHandle: true));
        using RegistryKey? subKey = root.OpenSubKey(subKeyPath);

        return subKey?.GetSubKeyNames() ?? [];
    }

    /// <summary>
    ///     Reads a single value from a seeded hive through the LIVE Windows registry (<c>RegLoadAppKey</c>, read-only) so
    ///     a test can assert the managed <see cref="OfflineHiveFile" /> parser returns the byte-identical boxed type and value
    ///     that <see cref="RegistryKey.GetValue(string?)" /> would. The hive is loaded, read, and unloaded synchronously, so
    ///     the file is fully released before a caller memory-maps it - call this BEFORE opening the same hive with
    ///     <see cref="OfflineHiveFile" /> to avoid a sharing conflict.
    /// </summary>
    public static object? ReadValueViaLiveRegistry(string hivePath, string subKeyPath, string? valueName)
    {
        int result = RegLoadAppKey(hivePath, out nint handle, KeyRead, 0, 0);

        if (result != 0)
        {
            throw new InvalidOperationException($"RegLoadAppKey (read) failed for {hivePath} (error {result}).");
        }

        using RegistryKey root = RegistryKey.FromHandle(new SafeRegistryHandle(handle, ownsHandle: true));
        using RegistryKey? subKey = root.OpenSubKey(subKeyPath);

        return subKey?.GetValue(valueName, defaultValue: null, RegistryValueOptions.DoNotExpandEnvironmentNames);
    }

    /// <summary>
    ///     Creates an NTFS directory junction (which, unlike symbolic links, does not require elevation) for
    ///     reparse-point tests; returns false when the platform refuses so callers can <c>Assert.SkipUnless</c>.
    /// </summary>
    public static bool TryCreateJunction(string junctionPath, string targetPath)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junctionPath}\" \"{targetPath}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (process is null) { return false; }

            process.WaitForExit(10_000);

            return process.HasExited && process.ExitCode == 0 && Directory.Exists(junctionPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(RootDirectory)) { Directory.Delete(RootDirectory, recursive: true); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup of the throwaway scaffold.
        }
    }

    // Test-only P/Invoke. Production deleted its registry-load interop when the offline reader moved to the managed
    // regf parser (OfflineHiveFile); the tests still need RegLoadAppKey to MATERIALIZE a real standalone hive on disk
    // (the test process has no package identity, so the create-and-seed direction works) that OfflineHiveFile then
    // parses back. RegLoadAppKey creates the file if it does not exist and returns a writable HKEY for seeding.
    [LibraryImport("advapi32.dll", EntryPoint = "RegLoadAppKeyW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegLoadAppKey(string lpFile, out nint phkResult, int samDesired, int dwOptions, int reserved);

    private static void SeedOrTouchHive(string hivePath, Action<RegistryKey>? seed)
    {
        if (seed is null)
        {
            // An empty placeholder is sufficient for components that only inspect paths and never load the hive.
            File.WriteAllBytes(hivePath, []);

            return;
        }

        int result = RegLoadAppKey(hivePath, out nint handle, KeyAllAccess, 0, 0);

        if (result != 0)
        {
            throw new InvalidOperationException($"RegLoadAppKey failed to create the test hive {hivePath} (error {result}).");
        }

        using RegistryKey root = RegistryKey.FromHandle(new SafeRegistryHandle(handle, ownsHandle: true));
        seed(root);
        root.Flush();
    }
}
