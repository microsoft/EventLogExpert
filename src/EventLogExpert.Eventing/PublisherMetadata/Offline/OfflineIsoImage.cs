// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;

namespace EventLogExpert.Eventing.PublisherMetadata.Offline;

/// <summary>The result of <see cref="OfflineIsoImage.TryMount(string, ITraceLogger?)" />: the outcome plus the mount.</summary>
public readonly record struct OfflineIsoMountResult(OfflineIsoMountStatus Status, OfflineIsoImage? Image)
{
    internal static OfflineIsoMountResult Failed(OfflineIsoMountStatus status) => new(status, null);
}

/// <summary>
///     Mounts a Windows <c>.iso</c> read-only and exposes its <c>sources\install.wim</c> (else <c>install.esd</c>) so
///     the existing WIM extractor can read it; an ISO is just a front-end producing a WIM. The mount detaches on
///     <see cref="Dispose" />. The entry point NEVER throws for a bad / non-Windows / non-ISO file - it returns a typed
///     <see cref="OfflineIsoMountStatus" />.
/// </summary>
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

    /// <summary>
    ///     Path to the install image inside the mounted ISO; pass to the WIM extractor. Valid until
    ///     <see cref="Dispose" />.
    /// </summary>
    public string InstallImagePath { get; }

    public static OfflineIsoMountResult TryMount(string isoPath, ITraceLogger? logger) =>
        TryMount(isoPath, VirtualDiskOperations.Instance, logger);

    public void Dispose()
    {
        if (_disposed) { return; }

        _disposed = true;

        _lease.Dispose();
    }

    internal static OfflineIsoMountResult TryMount(string isoPath, IVirtualDiskOperations VirtualDiskOperations, ITraceLogger? logger)
    {
        if (!File.Exists(isoPath))
        {
            logger?.Debug($"{nameof(OfflineIsoImage)}: ISO file not found: {isoPath}.");

            return OfflineIsoMountResult.Failed(OfflineIsoMountStatus.NotAnIso);
        }

        IsoAttachResult attach = VirtualDiskOperations.Attach(isoPath, logger);

        if (attach.Status != IsoAttachStatus.Attached || attach.VolumeRoot is null || attach.Lease is null)
        {
            return OfflineIsoMountResult.Failed(
                attach.Status == IsoAttachStatus.NotAnIso ? OfflineIsoMountStatus.NotAnIso : OfflineIsoMountStatus.MountFailed);
        }

        // install.wim first, then install.esd. The path is built from exact segments under the resolved volume - never a
        // scan - so a crafted ISO cannot redirect the lookup outside the mount.
        string? installImagePath = ResolveInstallImage(attach.VolumeRoot);

        if (installImagePath is null)
        {
            attach.Lease.Dispose();
            logger?.Debug($"{nameof(OfflineIsoImage)}: no sources\\install.wim or install.esd on {isoPath}.");

            return OfflineIsoMountResult.Failed(OfflineIsoMountStatus.NoInstallImage);
        }

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
