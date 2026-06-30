// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;

namespace EventLogExpert.Eventing.PublisherMetadata.Offline;

/// <summary>
///     Helper-side startup maintenance for offline-image scratch resources. Reclaims orphaned <c>ELX_WIM_*</c>
///     extraction folders in <see cref="OfflineScratch.Root" /> a crashed or self-terminated prior run left behind,
///     identified by a dead machine-global ownership beacon so a live sibling run's folders are never reclaimed.
/// </summary>
public static class OfflineMaintenance
{
    /// <summary>Reclaims orphaned WIM extraction folders. Best-effort; never throws.</summary>
    public static void ReconcileOrphans(ITraceLogger? logger)
    {
        OfflineWimImage.ReconcileOrphanedExtractions(logger);
    }
}
