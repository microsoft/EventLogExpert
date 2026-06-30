// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;

namespace EventLogExpert.Eventing.PublisherMetadata.Offline;

public static class OfflineMaintenance
{
    public static void ReconcileOrphans(ITraceLogger? logger)
    {
        OfflineWimImage.ReconcileOrphanedExtractions(logger);
    }
}
