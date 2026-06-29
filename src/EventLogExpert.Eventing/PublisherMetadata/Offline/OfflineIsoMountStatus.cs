// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.PublisherMetadata.Offline;

/// <summary>The outcome of mounting an ISO and locating its install image; only Mounted yields a usable image.</summary>
public enum OfflineIsoMountStatus
{
    Mounted,
    NotAnIso,
    NoInstallImage,
    MountFailed
}
