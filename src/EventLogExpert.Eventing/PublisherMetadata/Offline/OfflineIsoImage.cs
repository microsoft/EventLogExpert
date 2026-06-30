// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;

namespace EventLogExpert.Eventing.PublisherMetadata.Offline;

public readonly record struct OfflineIsoMountResult(OfflineIsoMountStatus Status, OfflineIsoImage? Image)
{
    internal static OfflineIsoMountResult Failed(OfflineIsoMountStatus status) => new(status, null);
}

public sealed class OfflineIsoImage : IDisposable
{
    private readonly IDisposable _lease;
    private readonly ITraceLogger? _logger;
    private bool _disposed;

    private OfflineIsoImage(string installImagePath, IDisposable lease, ITraceLogger? logger)
    {
        InstallImagePath = installImagePath;
        _lease = lease;
        _logger = logger;
    }

    public string InstallImagePath { get; }

    public static OfflineIsoMountResult TryMount(string isoPath, ITraceLogger? logger) =>
        TryMount(isoPath, VirtualDiskOperations.Instance, logger);

    public void Dispose()
    {
        if (_disposed) { return; }

        _disposed = true;

        _lease.Dispose();
    }

    internal static OfflineIsoMountResult TryMount(string isoPath, IVirtualDiskOperations virtualDiskOperations, ITraceLogger? logger)
    {
        if (!File.Exists(isoPath))
        {
            logger?.Debug($"{nameof(OfflineIsoImage)}: ISO file not found: {isoPath}.");

            return OfflineIsoMountResult.Failed(OfflineIsoMountStatus.NotAnIso);
        }

        logger?.Information($"Mounting ISO {isoPath}...");

        IsoAttachResult attach = virtualDiskOperations.Attach(isoPath, logger);

        if (attach.Status != IsoAttachStatus.Attached || attach.VolumeRoot is null || attach.Lease is null)
        {
            return OfflineIsoMountResult.Failed(
                attach.Status == IsoAttachStatus.NotAnIso ? OfflineIsoMountStatus.NotAnIso : OfflineIsoMountStatus.MountFailed);
        }

        // Build exact install-image paths under the mounted volume; never scan or follow image-provided redirects.
        string? installImagePath = ResolveInstallImage(attach.VolumeRoot);

        if (installImagePath is null)
        {
            attach.Lease.Dispose();
            logger?.Debug($"{nameof(OfflineIsoImage)}: no sources\\install.wim or install.esd on {isoPath}.");

            return OfflineIsoMountResult.Failed(OfflineIsoMountStatus.NoInstallImage);
        }

        logger?.Information($"Mounted ISO at {attach.VolumeRoot}.");

        return new OfflineIsoMountResult(OfflineIsoMountStatus.Mounted, new OfflineIsoImage(installImagePath, attach.Lease, logger));
    }

    private static string? ResolveInstallImage(string volumeRoot)
    {
        string wim = Path.Join(volumeRoot, "sources", "install.wim");

        if (File.Exists(wim)) { return wim; }

        string esd = Path.Join(volumeRoot, "sources", "install.esd");

        return File.Exists(esd) ? esd : null;
    }
}
