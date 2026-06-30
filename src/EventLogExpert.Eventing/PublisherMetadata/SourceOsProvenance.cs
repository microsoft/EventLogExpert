// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.PublisherMetadata.Offline;
using EventLogExpert.Logging.Abstractions;
using Microsoft.Win32;

namespace EventLogExpert.Eventing.PublisherMetadata;

// Stored per provider so recency can prefer newer OS sources without trusting database filenames.
public sealed record SourceOsProvenance(int? Build, int? Revision, string? Edition, string? DisplayVersion)
{
    public static SourceOsProvenance Empty { get; } = new(null, null, null, null);

    public static SourceOsProvenance Read(ITraceLogger? logger = null)
    {
        try
        {
            // Owned base key: Registry.LocalMachine is shared static state and must not be disposed per instance.
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
            using var currentVersion = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");

            return ParseCurrentVersion(
                currentVersion is null ? null : new LiveRegistryKeyAdapter(currentVersion, ownsKey: false), logger);
        }
        catch (Exception ex)
        {
            logger?.Debug($"{nameof(SourceOsProvenance)}: failed to read host OS provenance. Exception:\n{ex}");

            return Empty;
        }
    }

    // Offline builds stamp the image OS from the loaded SOFTWARE hive, never the host registry.
    internal static SourceOsProvenance ReadFromSoftwareHive(IOfflineRegistryKey softwareRoot, ITraceLogger? logger = null)
    {
        try
        {
            using var currentVersion = softwareRoot.OpenSubKey(@"Microsoft\Windows NT\CurrentVersion");

            return ParseCurrentVersion(currentVersion, logger);
        }
        catch (Exception ex)
        {
            logger?.Debug($"{nameof(SourceOsProvenance)}: failed to read image OS provenance. Exception:\n{ex}");

            return Empty;
        }
    }

    private static SourceOsProvenance ParseCurrentVersion(IOfflineRegistryKey? currentVersion, ITraceLogger? logger)
    {
        if (currentVersion is null)
        {
            logger?.Debug($"{nameof(SourceOsProvenance)}: CurrentVersion key not found; provenance unavailable.");

            return Empty;
        }

        var build = int.TryParse(ReadRawString(currentVersion, "CurrentBuildNumber"), out var parsedBuild)
            ? parsedBuild
            : (int?)null;

        var revision = currentVersion.GetValue("UBR") is int rawRevision ? rawRevision : (int?)null;
        var edition = ReadRawString(currentVersion, "EditionID");
        var displayVersion = ReadRawString(currentVersion, "DisplayVersion");

        return new SourceOsProvenance(build, revision, edition, displayVersion);
    }

    // Foreign-image REG_EXPAND_SZ values must stay literal instead of expanding against the host environment.
    private static string? ReadRawString(IOfflineRegistryKey key, string valueName) => key.GetValue(valueName) as string;
}
