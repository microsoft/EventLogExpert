// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using Microsoft.Win32;

namespace EventLogExpert.Eventing.PublisherMetadata;

/// <summary>
///     Provenance of the OS a provider database is built from - the host (live build) or a foreign image (offline
///     build) - read from <c>…\Microsoft\Windows NT\CurrentVersion</c>. Recorded per provider row so resolution can
///     prefer the newest source (the recency tiebreak) without relying on the database file name. All fields are null
///     when the key cannot be read; resolution degrades gracefully to completeness + load order.
/// </summary>
public sealed record SourceOsProvenance(int? Build, int? Revision, string? Edition, string? DisplayVersion)
{
    public static SourceOsProvenance Empty { get; } = new(null, null, null, null);

    /// <summary>Reads the host OS provenance from the local <c>HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion</c>.</summary>
    public static SourceOsProvenance Read(ITraceLogger? logger = null)
    {
        try
        {
            // Open an owned base key (do NOT use Registry.LocalMachine - that's a shared static), matching
            // RegistryProvider so concurrent instances dispose independently.
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
            using var currentVersion = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");

            return ParseCurrentVersion(currentVersion, logger);
        }
        catch (Exception ex)
        {
            logger?.Debug($"{nameof(SourceOsProvenance)}: failed to read host OS provenance. Exception:\n{ex}");

            return Empty;
        }
    }

    /// <summary>
    ///     Reads a foreign image's OS provenance from its already-loaded <c>SOFTWARE</c> hive root (the
    ///     <c>Microsoft\Windows NT\CurrentVersion</c> subkey), so an offline image build stamps rows with the IMAGE's OS
    ///     rather than the host's. Never touches the host registry. Internal: the offline extraction path is the only
    ///     caller; cross-assembly consumers use the host <see cref="Read" /> overload.
    /// </summary>
    internal static SourceOsProvenance ReadFromSoftwareHive(RegistryKey softwareRoot, ITraceLogger? logger = null)
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

    // Read the raw stored strings WITHOUT environment expansion: .NET's RegistryKey.GetValue expands a
    // REG_EXPAND_SZ value against the HOST environment by default, so a foreign or malformed image SOFTWARE hive
    // that stored these as REG_EXPAND_SZ would otherwise contaminate offline provenance with host data (and the
    // literal stored value is what provenance wants regardless). The host Read() path is unaffected - these are
    // REG_SZ there - and UBR is a DWORD, so expansion never applies to it.
    private static SourceOsProvenance ParseCurrentVersion(RegistryKey? currentVersion, ITraceLogger? logger)
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

    private static string? ReadRawString(RegistryKey key, string valueName) =>
        key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
}
