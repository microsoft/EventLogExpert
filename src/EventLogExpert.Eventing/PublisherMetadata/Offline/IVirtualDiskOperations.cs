// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;

namespace EventLogExpert.Eventing.PublisherMetadata.Offline;

/// <summary>Status of a raw ISO attach (before the install-image lookup that turns it into a mount).</summary>
internal enum IsoAttachStatus { Attached, NotAnIso, Failed }

/// <summary>An attached ISO: the volume root path to probe plus a lease that detaches the disk on dispose.</summary>
internal readonly record struct IsoAttachResult(IsoAttachStatus Status, string? VolumeRoot, IDisposable? Lease)
{
    internal static IsoAttachResult Failed(IsoAttachStatus status) => new(status, null, null);
}

/// <summary>
///     Status of a raw VHD/VHDX attach. <see cref="NoWindowsVolume" /> means no readable partition with
///     <c>\Windows\System32</c> was found (e.g. a data-only or BitLocker-locked disk);
///     <see cref="MultipleWindowsVolumes" /> means more than one was found, which is a deliberate ambiguity error rather
///     than a silent guess.
/// </summary>
internal enum VhdxAttachStatus { Attached, NotAVhdx, NoWindowsVolume, MultipleWindowsVolumes, Failed }

/// <summary>An attached VHD/VHDX: the resolved Windows volume root plus a lease that detaches the disk on dispose.</summary>
internal readonly record struct VhdxAttachResult(VhdxAttachStatus Status, string? VolumeRoot, IDisposable? Lease)
{
    internal static VhdxAttachResult Failed(VhdxAttachStatus status) => new(status, null, null);
}

/// <summary>
///     The native attach/detach operations <see cref="OfflineIsoImage" /> and <see cref="OfflineVhdxImage" />
///     orchestrate, behind an interface so the mount / find-volume / cleanup state machine can be unit-tested with a fake;
///     the real path needs a real disk. COARSE on purpose: no native handle crosses it - the volume handle lifetime stays
///     inside the lease.
/// </summary>
internal interface IVirtualDiskOperations
{
    IsoAttachResult Attach(string isoPath, ITraceLogger? logger);

    VhdxAttachResult AttachVhdx(string vhdxPath, ITraceLogger? logger);
}
