// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.PublisherMetadata.Offline;

public enum OfflineVhdxMountStatus
{
    Mounted,
    NotAVhdx,
    NoWindowsVolume,
    MultipleWindowsVolumes,
    MountFailed
}
