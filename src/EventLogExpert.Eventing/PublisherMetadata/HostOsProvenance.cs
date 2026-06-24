// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using Microsoft.Win32;

namespace EventLogExpert.Eventing.PublisherMetadata;

/// <summary>
///     Provenance of the host OS a provider database is built from, read once per db-create from the local
///     <c>HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion</c> hive. Recorded per provider row so resolution can prefer
///     the newest source (the recency tiebreak) without relying on the database file name. All fields are null when the
///     hive cannot be read; resolution degrades gracefully to completeness + load order.
/// </summary>
public sealed record HostOsProvenance(int? Build, int? Revision, string? Edition, string? DisplayVersion)
{
    public static HostOsProvenance Empty { get; } = new(null, null, null, null);

    public static HostOsProvenance Read(ITraceLogger? logger = null)
    {
        try
        {
            // Open an owned base key (do NOT use Registry.LocalMachine - that's a shared static), matching
            // RegistryProvider so concurrent instances dispose independently.
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);

            using var currentVersion = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");

            if (currentVersion is null)
            {
                logger?.Debug($"{nameof(HostOsProvenance)}: CurrentVersion key not found; provenance unavailable.");

                return Empty;
            }

            // CurrentBuildNumber is a REG_SZ; UBR is a DWORD; EditionID / DisplayVersion are REG_SZ.
            var build = int.TryParse(currentVersion.GetValue("CurrentBuildNumber") as string, out var parsedBuild)
                ? parsedBuild
                : (int?)null;

            var revision = currentVersion.GetValue("UBR") is int rawRevision ? rawRevision : (int?)null;
            var edition = currentVersion.GetValue("EditionID") as string;
            var displayVersion = currentVersion.GetValue("DisplayVersion") as string;

            return new HostOsProvenance(build, revision, edition, displayVersion);
        }
        catch (Exception ex)
        {
            logger?.Debug($"{nameof(HostOsProvenance)}: failed to read host OS provenance. Exception:\n{ex}");

            return Empty;
        }
    }
}
