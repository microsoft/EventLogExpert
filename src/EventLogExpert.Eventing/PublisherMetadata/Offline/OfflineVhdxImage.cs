// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;

namespace EventLogExpert.Eventing.PublisherMetadata.Offline;

/// <summary>The result of <see cref="OfflineVhdxImage.TryMount(string, ITraceLogger?)" />: the outcome plus the mount.</summary>
public readonly record struct OfflineVhdxMountResult(OfflineVhdxMountStatus Status, OfflineVhdxImage? Image)
{
    internal static OfflineVhdxMountResult Failed(OfflineVhdxMountStatus status) => new(status, null);
}

/// <summary>
///     Attaches a <c>.vhdx</c>/<c>.vhd</c> read-only with no drive letter, resolves the partition holding the offline
///     Windows installation (<c>\Windows\System32</c>), and exposes that volume root so the existing directory-image
///     extractor can read it; a VHD/VHDX is just a front-end producing a mounted volume. The mount detaches on
///     <see cref="Dispose" />. The entry point NEVER throws for a bad / non-Windows / non-VHDX file - it returns a typed
///     <see cref="OfflineVhdxMountStatus" />.
/// </summary>
public sealed class OfflineVhdxImage : IDisposable
{
    private readonly IDisposable _lease;
    private bool _disposed;

    private OfflineVhdxImage(string volumeRoot, IDisposable lease)
    {
        VolumeRoot = volumeRoot;
        _lease = lease;
    }

    /// <summary>
    ///     Root of the mounted Windows volume (a volume-GUID path); read it as a directory image. Valid until
    ///     <see cref="Dispose" />.
    /// </summary>
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

            return OfflineVhdxMountResult.Failed(OfflineVhdxMountStatus.NotAVhdx);
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
