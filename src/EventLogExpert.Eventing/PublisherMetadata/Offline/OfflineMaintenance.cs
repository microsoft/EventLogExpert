// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;

namespace EventLogExpert.Eventing.PublisherMetadata.Offline;

/// <summary>
///     Helper-side startup maintenance for offline-image scratch resources. Reclaims anything a crashed or
///     self-terminated prior run left behind: orphaned dirty-hive <c>HKLM\ELX_*</c> recovery mounts and orphaned
///     <c>ELX_WIM_*</c> extraction folders in <see cref="OfflineScratch.Root" />. Both are identified by a dead
///     machine-global ownership beacon, so a live sibling run's resources are never reclaimed. Must run ELEVATED
///     (unmounting a hive needs backup/restore privilege), so it is invoked once at elevated-helper startup before an
///     operation is dispatched - never from the medium-IL host, which cannot unload an <c>HKLM</c> hive.
/// </summary>
public static class OfflineMaintenance
{
    /// <summary>Reclaims orphaned hive mounts and WIM extraction folders. Best-effort; never throws.</summary>
    public static void ReconcileOrphans(ITraceLogger? logger)
    {
        OfflineRegistryHive.SweepOrphanedMounts(logger);
        OfflineWimImage.ReconcileOrphanedExtractions(logger);
    }
}
