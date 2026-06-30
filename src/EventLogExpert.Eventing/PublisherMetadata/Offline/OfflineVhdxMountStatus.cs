// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.PublisherMetadata.Offline;

/// <summary>
///     The outcome of attaching a <c>.vhdx</c>/<c>.vhd</c> and locating its Windows volume; only
///     <see cref="Mounted" /> yields a usable image. <see cref="NoWindowsVolume" /> means no readable partition with
///     <c>\Windows\System32</c> was found (data-only or BitLocker-locked); <see cref="MultipleWindowsVolumes" /> means
///     more than one was found, reported as an explicit ambiguity rather than a silent guess.
/// </summary>
public enum OfflineVhdxMountStatus
{
    Mounted,
    NotAVhdx,
    NoWindowsVolume,
    MultipleWindowsVolumes,
    MountFailed
}
