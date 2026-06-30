// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Ipc;
using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.DatabaseTools.CreateDatabase;
using EventLogExpert.ElevationHelper.Ipc;
using EventLogExpert.Eventing.PublisherMetadata.Offline;
using EventLogExpert.Logging.Sinks;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace EventLogExpert.ElevationHelper.Operations;

/// <summary>
///     Helper-side handler for <see cref="ListImageEditionsIpcRequest" />: a read-only enumeration of an offline
///     image's editions. A <c>.wim</c>/<c>.esd</c> is read directly; a <c>.iso</c> is mounted to read its inner
///     <c>install.wim</c> and detached in the <c>finally</c>. On a readable image the editions are streamed back as an
///     <see cref="ImageEditionsMessage" /> BEFORE the terminal success result, so the runner always pairs a success with a
///     payload; an unreadable file or a failed mount surfaces as <see cref="DatabaseToolsOutcome.Failed" /> instead.
/// </summary>
/// <remarks>
///     This runs OUTSIDE <see cref="DestructiveRecovery" /> because it neither writes a database nor renames anything
///     - it only mounts (ISO) and reads. The ISO mount lifetime is bound to <see cref="OfflineIsoImage" />'s handle, so a
///     crash before <c>Dispose</c> still detaches when the handle is reclaimed (orphan-mount-safe).
/// </remarks>
internal static class ListImageEditionsHandler
{
    public static async Task<DatabaseToolsResult> HandleAsync(
        ListOfflineImageEditionsRequest request,
        IpcMessageWriter writer,
        bool verbose,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var logger = new StreamingTraceLogger(new IpcLogSink(writer), verbose ? LogLevel.Trace : LogLevel.Information);

        OfflineImageKind? kind = OfflineImageKindResolver.ResolveFromPath(request.ImagePath);

        if (kind is not (OfflineImageKind.Wim or OfflineImageKind.Iso))
        {
            return new DatabaseToolsResult(
                DatabaseToolsOutcome.Failed,
                $"Editions can only be listed for a .wim, .esd, or .iso file; '{request.ImagePath}' is not one of those.",
                stopwatch.Elapsed);
        }

        OfflineIsoImage? isoImage = null;

        try
        {
            string wimPath;

            if (kind is OfflineImageKind.Iso)
            {
                OfflineIsoMountResult mount = OfflineIsoImage.TryMount(request.ImagePath, logger);

                if (mount.Status != OfflineIsoMountStatus.Mounted)
                {
                    return new DatabaseToolsResult(DatabaseToolsOutcome.Failed, DescribeIsoMountFailure(mount.Status, request.ImagePath), stopwatch.Elapsed);
                }

                isoImage = mount.Image;
                wimPath = isoImage!.InstallImagePath;
            }
            else
            {
                wimPath = request.ImagePath;
            }

            cancellationToken.ThrowIfCancellationRequested();

            WimImageList imageList = OfflineWimImage.ReadIndexList(wimPath, logger);

            if (imageList.Status != WimImageListStatus.Ok)
            {
                return new DatabaseToolsResult(
                    DatabaseToolsOutcome.Failed,
                    $"The image '{request.ImagePath}' does not contain a readable Windows image (WIM/ESD) to list editions from.",
                    stopwatch.Elapsed);
            }

            await writer.WriteAsync(new ImageEditionsMessage(imageList.Status, imageList.Images), cancellationToken);

            return new DatabaseToolsResult(DatabaseToolsOutcome.Succeeded, FailureSummary: null, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            return new DatabaseToolsResult(DatabaseToolsOutcome.Cancelled, "Cancelled while listing image editions.", stopwatch.Elapsed);
        }
        finally
        {
            isoImage?.Dispose();
        }
    }

    private static string DescribeIsoMountFailure(OfflineIsoMountStatus status, string isoPath) => status switch
    {
        OfflineIsoMountStatus.NotAnIso => $"The file '{isoPath}' is not a valid ISO image.",
        OfflineIsoMountStatus.NoInstallImage => $"The ISO '{isoPath}' does not contain a sources\\install.wim or install.esd to list editions from.",
        OfflineIsoMountStatus.MountFailed => $"The ISO '{isoPath}' could not be mounted.",
        _ => $"The ISO '{isoPath}' could not be mounted (status: {status})."
    };
}
