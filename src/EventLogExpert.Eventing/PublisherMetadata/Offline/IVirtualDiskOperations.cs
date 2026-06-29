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
///     The native ISO attach/detach <see cref="OfflineIsoImage" /> orchestrates, behind an interface so the mount /
///     find-install-image / cleanup state machine can be unit-tested with a fake; the real path needs a real ISO. COARSE
///     on purpose: no native handle crosses it - the volume handle lifetime stays inside the lease.
/// </summary>
internal interface IVirtualDiskOperations
{
    IsoAttachResult Attach(string isoPath, ITraceLogger? logger);
}
