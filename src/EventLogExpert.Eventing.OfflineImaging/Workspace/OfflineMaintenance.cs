// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.OfflineImaging.Wim;
using EventLogExpert.Logging.Abstractions;

namespace EventLogExpert.Eventing.OfflineImaging.Workspace;

public static class OfflineMaintenance
{
    public static void ReconcileOrphans(ITraceLogger? logger)
    {
        OfflineWimImage.ReconcileOrphanedExtractions(logger);
    }
}
