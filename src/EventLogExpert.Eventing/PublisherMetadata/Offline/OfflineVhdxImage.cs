// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;

namespace EventLogExpert.Eventing.PublisherMetadata.Offline;

public readonly record struct OfflineVhdxMountResult(OfflineVhdxMountStatus Status, OfflineVhdxImage? Image)
{
    internal static OfflineVhdxMountResult Failed(OfflineVhdxMountStatus status) => new(status, null);
}

public sealed class OfflineVhdxImage : IDisposable
{
    private readonly IDisposable _lease;
    private bool _disposed;

    private OfflineVhdxImage(string volumeRoot, IDisposable lease)
    {
        VolumeRoot = volumeRoot;
        _lease = lease;
    }

    public string VolumeRoot { get; }

    public static OfflineVhdxMountResult TryMount(string vhdxPath, ITraceLogger? logger) =>
        TryMount(vhdxPath, VirtualDiskOperations.Instance, logger);

    public void Dispose()
    {
        if (_disposed) { return; }

        _disposed = true;

        _lease.Dispose();
    }

    internal static OfflineVhdxMountResult TryMount(string vhdxPath, IVirtualDiskOperations virtualDiskOperations, ITraceLogger? logger)
    {
        if (!File.Exists(vhdxPath))
        {
            logger?.Debug($"{nameof(OfflineVhdxImage)}: VHD/VHDX file not found: {vhdxPath}.");

            // A missing file is an open/mount failure, not a format problem; NotAVhdx would surface as "not a readable VHD/VHDX".
            return OfflineVhdxMountResult.Failed(OfflineVhdxMountStatus.MountFailed);
        }

        logger?.Information($"Mounting VHD/VHDX {vhdxPath}...");

        VhdxAttachResult attach = virtualDiskOperations.AttachVhdx(vhdxPath, logger);

        switch (attach.Status)
        {
            case VhdxAttachStatus.Attached when attach.VolumeRoot is not null && attach.Lease is not null:
                logger?.Information($"Mounted VHD/VHDX at {attach.VolumeRoot}.");

                return new OfflineVhdxMountResult(OfflineVhdxMountStatus.Mounted, new OfflineVhdxImage(attach.VolumeRoot, attach.Lease));
            case VhdxAttachStatus.NotAVhdx:
                return OfflineVhdxMountResult.Failed(OfflineVhdxMountStatus.NotAVhdx);
            case VhdxAttachStatus.NoWindowsVolume:
                return OfflineVhdxMountResult.Failed(OfflineVhdxMountStatus.NoWindowsVolume);
            case VhdxAttachStatus.MultipleWindowsVolumes:
                return OfflineVhdxMountResult.Failed(OfflineVhdxMountStatus.MultipleWindowsVolumes);
            default:
                return OfflineVhdxMountResult.Failed(OfflineVhdxMountStatus.MountFailed);
        }
    }
}
