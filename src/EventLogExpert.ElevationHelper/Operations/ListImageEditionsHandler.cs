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

internal static class ListImageEditionsHandler
{
    public static async Task<DatabaseToolsResult> HandleAsync(
        ListOfflineImageEditionsRequest request,
        IpcMessageWriter writer,
        bool verbose,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var logger = new StreamingTraceLogger(new IpcLogForwarder(writer), verbose ? LogLevel.Trace : LogLevel.Information);

        OfflineImageKind? kind = OfflineImageKindResolver.ResolveFromPath(request.ImagePath);

        if (kind is not (OfflineImageKind.Wim or OfflineImageKind.Iso))
        {
            return new DatabaseToolsResult(
                DatabaseToolsOutcome.Failed,
                $"Editions can only be listed for a .wim, .esd, or .iso file; '{request.ImagePath}' is not one of those.",
                stopwatch.Elapsed);
        }

        if (!File.Exists(request.ImagePath))
        {
            var imageKindName = kind is OfflineImageKind.Iso ? "ISO" : "WIM";

            return new DatabaseToolsResult(
                DatabaseToolsOutcome.Failed,
                $"{imageKindName} image file not found: {request.ImagePath}",
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
