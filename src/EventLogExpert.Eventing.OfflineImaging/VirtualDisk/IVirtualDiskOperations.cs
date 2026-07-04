// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;

namespace EventLogExpert.Eventing.OfflineImaging.VirtualDisk;

internal enum IsoAttachStatus { Attached, NotAnIso, Failed }

internal readonly record struct IsoAttachResult(IsoAttachStatus Status, string? VolumeRoot, IDisposable? Lease)
{
    internal static IsoAttachResult Failed(IsoAttachStatus status) => new(status, null, null);
}

internal enum VhdxAttachStatus { Attached, NotAVhdx, NoWindowsVolume, MultipleWindowsVolumes, Failed }

internal readonly record struct VhdxAttachResult(VhdxAttachStatus Status, string? VolumeRoot, IDisposable? Lease)
{
    internal static VhdxAttachResult Failed(VhdxAttachStatus status) => new(status, null, null);
}

// Coarse seam keeps native handle lifetime inside the real lease while tests fake the attach state machine.
internal interface IVirtualDiskOperations
{
    IsoAttachResult Attach(string isoPath, ITraceLogger? logger);

    VhdxAttachResult AttachVhdx(string vhdxPath, ITraceLogger? logger);
}
