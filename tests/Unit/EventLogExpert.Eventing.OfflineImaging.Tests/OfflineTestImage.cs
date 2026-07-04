// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.OfflineImaging.Containment;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.OfflineImaging.Tests;

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

    // Test-only RegLoadAppKey materializes seed hives for OfflineHiveFile without reviving production registry-load interop.
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
