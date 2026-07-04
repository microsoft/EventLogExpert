// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.OfflineImaging.VirtualDisk;

public enum OfflineVhdxMountStatus
{
    Mounted,
    NotAVhdx,
    NoWindowsVolume,
    MultipleWindowsVolumes,
    MountFailed
}
