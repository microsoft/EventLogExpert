// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Interop;
using EventLogExpert.Eventing.PublisherMetadata.Offline.Containment;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace EventLogExpert.Eventing.Tests.PublisherMetadata.Offline;

/// <summary>
///     Builds a throwaway on-disk Windows-image scaffold (
///     <c>&lt;temp&gt;\Windows\System32\config\{SOFTWARE,SYSTEM}</c>) for offline-extraction unit tests, with no admin and
///     no real Windows image. Each hive is either an empty placeholder (enough for path-only components such as the mapper
///     and root-guard) or a real standalone hive seeded via <see cref="NativeMethods.RegLoadAppKey" /> (for the catalog
///     and legacy-resolver readers). The scaffold's root directory is the image root, so a mapped host path such as
///     <c>C:\Windows\System32\foo.dll</c> correctly lands under the scaffold - never the host - even though the scaffold
///     itself lives on the host drive.
/// </summary>
internal sealed class OfflineTestImage : IDisposable
{
    private const int KeyAllAccess = 0xF003F;

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

    private static void SeedOrTouchHive(string hivePath, Action<RegistryKey>? seed)
    {
        if (seed is null)
        {
            // An empty placeholder is sufficient for components that only inspect paths and never load the hive.
            File.WriteAllBytes(hivePath, []);

            return;
        }

        int result = NativeMethods.RegLoadAppKey(hivePath, out nint handle, KeyAllAccess, 0, 0);

        if (result != 0)
        {
            throw new InvalidOperationException($"RegLoadAppKey failed to create the test hive {hivePath} (error {result}).");
        }

        using RegistryKey root = RegistryKey.FromHandle(new SafeRegistryHandle(handle, ownsHandle: true));
        seed(root);
        root.Flush();
    }
}
