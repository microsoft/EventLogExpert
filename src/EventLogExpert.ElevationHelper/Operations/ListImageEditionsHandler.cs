// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common;
using EventLogExpert.DatabaseTools.Common.Ipc;
using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.DatabaseTools.CreateDatabase;
using EventLogExpert.ElevationHelper.Ipc;
using EventLogExpert.Eventing.OfflineImaging.Iso;
using EventLogExpert.Eventing.OfflineImaging.Wim;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Loggers;
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

        IOperationLog logger = new StreamingOperationLog(
            new IpcLogForwarder(writer), verbose ? LogLevel.Trace : LogLevel.Information);

        OfflineImageKind? kind = OfflineImageKindResolver.ResolveFromPath(request.ImagePath);

        if (kind is not (OfflineImageKind.Wim or OfflineImageKind.Iso))
        {
            return new DatabaseToolsResult(
                DatabaseToolsOutcome.Failed,
                new LocalizableText(DatabaseToolsLogKeys.EditionsUnsupportedImageFile, [request.ImagePath]),
                stopwatch.Elapsed);
        }

        if (!File.Exists(request.ImagePath))
        {
            var imageKindName = kind is OfflineImageKind.Iso ? "ISO" : "WIM";

            return new DatabaseToolsResult(
                DatabaseToolsOutcome.Failed,
                new LocalizableText(kind is OfflineImageKind.Iso ?
                        DatabaseToolsLogKeys.EditionsIsoFileNotFound :
                        DatabaseToolsLogKeys.EditionsWimFileNotFound,
                    [request.ImagePath]),
                stopwatch.Elapsed);
        }

        OfflineIsoImage? isoImage = null;

        try
        {
            string wimPath;

            if (kind is OfflineImageKind.Iso)
            {
                OfflineIsoMountResult mount = OfflineIsoImage.TryMount(request.ImagePath,
                    logger.ForCategory(LogCategories.OfflineIso).Trace);

                if (mount.Status != OfflineIsoMountStatus.Mounted)
                {
                    return new DatabaseToolsResult(DatabaseToolsOutcome.Failed,
                        DescribeIsoMountFailure(mount.Status, request.ImagePath),
                        stopwatch.Elapsed);
                }

                isoImage = mount.Image;
                wimPath = isoImage!.InstallImagePath;
            }
            else
            {
                wimPath = request.ImagePath;
            }

            cancellationToken.ThrowIfCancellationRequested();

            WimImageList imageList =
                OfflineWimImage.ReadIndexList(wimPath, logger.ForCategory(LogCategories.OfflineWim).Trace);

            if (imageList.Status != WimImageListStatus.Ok)
            {
                return new DatabaseToolsResult(
                    DatabaseToolsOutcome.Failed,
                    new LocalizableText(DatabaseToolsLogKeys.EditionsNoReadableWindowsImage, [request.ImagePath]),
                    stopwatch.Elapsed);
            }

            await writer.WriteAsync(new ImageEditionsMessage(imageList.Status, imageList.Images), cancellationToken);

            return new DatabaseToolsResult(DatabaseToolsOutcome.Succeeded, null, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            return new DatabaseToolsResult(
                DatabaseToolsOutcome.Cancelled,
                new LocalizableText(DatabaseToolsLogKeys.EditionsCancelled, []),
                stopwatch.Elapsed);
        }
        finally
        {
            isoImage?.Dispose();
        }
    }

    private static LocalizableText DescribeIsoMountFailure(OfflineIsoMountStatus status, string isoPath) =>
        status switch
        {
            OfflineIsoMountStatus.NotAnIso => new LocalizableText(DatabaseToolsLogKeys.EditionsIsoNotValid, [isoPath]),
            OfflineIsoMountStatus.NoInstallImage => new LocalizableText(DatabaseToolsLogKeys.EditionsIsoNoInstallImage,
                [isoPath]),
            OfflineIsoMountStatus.MountFailed => new LocalizableText(DatabaseToolsLogKeys.EditionsIsoMountFailed,
                [isoPath]),
            _ => new LocalizableText(DatabaseToolsLogKeys.EditionsIsoMountFailedWithStatus,
                [isoPath, status.ToString()])
        };
}
