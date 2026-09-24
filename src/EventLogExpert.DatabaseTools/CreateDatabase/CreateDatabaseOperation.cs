// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common;
using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.Eventing.OfflineImaging;
using EventLogExpert.Eventing.OfflineImaging.Iso;
using EventLogExpert.Eventing.OfflineImaging.VirtualDisk;
using EventLogExpert.Eventing.OfflineImaging.Wim;
using EventLogExpert.Eventing.OfflineImaging.Workspace;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Database.Context;
using EventLogExpert.Provider.Database.Hashing;
using EventLogExpert.Provider.Resolution;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace EventLogExpert.DatabaseTools.CreateDatabase;

internal sealed class CreateDatabaseOperation(CreateDatabaseRequest request) : OperationBase, IDatabaseToolsOperation
{
    private const int BatchSize = 100;

    private static readonly string[] s_databaseFileSuffixes = ["", "-wal", "-shm"];

    private bool _overwriteBackupCompleted;
    private bool _overwriteBackupTaken;

    internal enum CreateDatabaseMode { Local, FileSource, OfflineImage }

    public async Task<DatabaseToolsOutcome> ExecuteAsync(
        IOperationLog logger,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(Path.GetExtension(request.TargetPath), ".db", StringComparison.OrdinalIgnoreCase))
        {
            logger.User(LogLevel.Error, new LocalizableText(DatabaseToolsLogKeys.CreateTargetExtensionMustBeDb, []));

            return DatabaseToolsOutcome.Failed;
        }

        foreach (var suffix in s_databaseFileSuffixes)
        {
            var backupPath = request.TargetPath + suffix + ".bak";

            if (File.Exists(backupPath))
            {
                logger.User(LogLevel.Error,
                    new LocalizableText(DatabaseToolsLogKeys.CreateRecoveryBackupExists, [backupPath]));

                return DatabaseToolsOutcome.Failed;
            }
        }

        if (File.Exists(request.TargetPath) && !request.Overwrite)
        {
            logger.User(LogLevel.Error,
                new LocalizableText(DatabaseToolsLogKeys.CreateTargetAlreadyExists, [request.TargetPath]));

            return DatabaseToolsOutcome.Failed;
        }

        if (!ValidateOfflineImageRequest(request, logger) ||
            (request.SourcePath is not null && !ProviderSource.TryValidate(request.SourcePath, logger)))
        {
            return DatabaseToolsOutcome.Failed;
        }

        string targetDirectory = Path.GetDirectoryName(Path.GetFullPath(request.TargetPath)) ?? request.TargetPath;
        OfflineWriteProbeResult targetBlocked = OfflineScratch.ProbeWritable(targetDirectory);

        if (!targetBlocked.IsWritable)
        {
            var summary = MapWritableProbeFailure(targetBlocked);
            LogWritableProbeFailure(logger, targetBlocked, summary);
            SetFailureSummary(summary);

            return DatabaseToolsOutcome.Failed;
        }

        HashSet<string> excludeProviderNames = new(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(request.SkipProvidersInFile))
        {
            if (!ProviderSource.TryValidate(request.SkipProvidersInFile, logger))
            {
                return DatabaseToolsOutcome.Failed;
            }

            foreach (var name in await ProviderSource.LoadProviderNamesAsync(request.SkipProvidersInFile,
                logger,
                cancellationToken: cancellationToken))
            {
                excludeProviderNames.Add(name);
            }

            logger.User(LogLevel.Information,
                new LocalizableText(
                    excludeProviderNames.Count == 1 ?
                        DatabaseToolsLogKeys.CreateSkippedProvidersOne :
                        DatabaseToolsLogKeys.CreateSkippedProvidersMany,
                    [excludeProviderNames.Count.ToString(), request.SkipProvidersInFile]));
        }

        var filterRegex = EnsureBoundedTimeout(request.FilterRegex);

        var outcome = await CreateCoreAsync();

        if (!_overwriteBackupTaken) { return outcome; }

        if (outcome is DatabaseToolsOutcome.Succeeded) { DeleteOverwriteBackups(logger); }
        else { RestoreOverwriteBackups(logger); }

        return outcome;

        async Task<DatabaseToolsOutcome> CreateCoreAsync()
        {
            var count = 0;
            var headerLogged = false;
            var pendingForHeader = new List<ProviderDetails>(BatchSize);

            var stampedIdentities = new HashSet<ProviderIdentity>();
#if DEBUG
            var firstByIdentity = new Dictionary<ProviderIdentity, ProviderDetails>();
#endif

            ProviderDbContext? dbContext = null;
            OfflineWimImage? wimImage = null;
            OfflineIsoImage? isoImage = null;
            OfflineVhdxImage? vhdxImage = null;

            try
            {
                var mode = SelectMode(request);

                string? effectiveOfflineImagePath = request.OfflineImagePath;
                OfflineImageKind? kind = mode == CreateDatabaseMode.OfflineImage ? ResolveImageKind(request) : null;

                if (kind is OfflineImageKind.Wim or OfflineImageKind.Iso)
                {
                    OfflineWriteProbeResult scratchBlocked = OfflineScratch.ProbeWritable(OfflineScratch.Root);

                    if (!scratchBlocked.IsWritable)
                    {
                        var summary = MapWritableProbeFailure(scratchBlocked);
                        LogWritableProbeFailure(logger.ForCategory(LogCategories.OfflineWim), scratchBlocked, summary);
                        SetFailureSummary(summary);

                        return DatabaseToolsOutcome.Failed;
                    }
                }

                if (kind is OfflineImageKind.Iso)
                {
                    IOperationLog isoLogger = logger.ForCategory(LogCategories.OfflineIso);
                    OfflineIsoMountResult mount = OfflineIsoImage.TryMount(request.OfflineImagePath!, isoLogger.Trace);

                    if (mount.Status != OfflineIsoMountStatus.Mounted)
                    {
                        return HandleIsoMountFailure(mount.Status, request.OfflineImagePath!, isoLogger);
                    }

                    isoImage = mount.Image;
                }

                if (kind is OfflineImageKind.Vhdx)
                {
                    IOperationLog vhdxLogger = logger.ForCategory(LogCategories.OfflineVhdx);

                    OfflineVhdxMountResult mount =
                        OfflineVhdxImage.TryMount(request.OfflineImagePath!, vhdxLogger.Trace);

                    if (mount.Status != OfflineVhdxMountStatus.Mounted)
                    {
                        return HandleVhdxMountFailure(mount.Status, request.OfflineImagePath!, vhdxLogger);
                    }

                    vhdxImage = mount.Image;
                    effectiveOfflineImagePath = vhdxImage!.VolumeRoot;
                }

                if (kind is OfflineImageKind.Wim or OfflineImageKind.Iso)
                {
                    IOperationLog wimLogger = logger.ForCategory(LogCategories.OfflineWim);
                    string wimSourcePath = isoImage?.InstallImagePath ?? request.OfflineImagePath!;

                    OfflineWimExtractResult extraction = await OfflineWimImage.TryExtractAsync(
                        wimSourcePath,
                        request.WimIndex!.Value,
                        OfflineScratch.Root,
                        wimLogger.Trace,
                        cancellationToken);

                    if (extraction.Status != OfflineWimExtractStatus.Extracted)
                    {
                        return HandleWimExtractionFailure(extraction.Status,
                            wimSourcePath,
                            request.WimIndex!.Value,
                            wimLogger);
                    }

                    wimImage = extraction.Image;
                    effectiveOfflineImagePath = wimImage!.ExtractedRoot;
                }

                IAsyncEnumerable<ProviderDetails> providersToAdd;
                SourceOsProvenance? sourceOsProvenance;

                switch (mode)
                {
                    case CreateDatabaseMode.OfflineImage:
                        providersToAdd = LoadOfflineImageProvidersAsync(effectiveOfflineImagePath!,
                            logger,
                            filterRegex,
                            excludeProviderNames,
                            cancellationToken);

                        sourceOsProvenance = null;

                        break;
                    case CreateDatabaseMode.Local:
                        providersToAdd =
                            LoadLocalProvidersAsync(logger, filterRegex, excludeProviderNames, cancellationToken);

                        sourceOsProvenance = SourceOsProvenance.Read(logger.Trace);

                        break;
                    default:
                        providersToAdd = ProviderSource.LoadProvidersAsync(request.SourcePath!,
                            logger,
                            filterRegex,
                            excludeProviderNames,
                            cancellationToken: cancellationToken);

                        sourceOsProvenance = null;

                        break;
                }

                await foreach (var details in providersToAdd.WithCancellation(cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    details.VersionKey = VersionKeyCalculator.Compute(details);

                    var identity = ProviderIdentity.Of(details);

                    if (!stampedIdentities.Add(identity))
                    {
#if DEBUG
                        AssertContentEquivalent(firstByIdentity[identity], details);
#endif

                        continue;
                    }

#if DEBUG
                    firstByIdentity[identity] = details;
#endif

                    if (sourceOsProvenance is not null)
                    {
                        details.SourceOsBuild = sourceOsProvenance.Build;
                        details.SourceOsRevision = sourceOsProvenance.Revision;
                        details.SourceOsEdition = sourceOsProvenance.Edition;
                        details.SourceOsDisplayVersion = sourceOsProvenance.DisplayVersion;
                    }

                    if (!headerLogged)
                    {
                        pendingForHeader.Add(details);

                        if (pendingForHeader.Count < BatchSize) { continue; }

                        dbContext ??= GetOrCreateContext();
                        count += pendingForHeader.Count;
                        await FlushHeaderAndBufferAsync(logger, dbContext, pendingForHeader, cancellationToken);
                        headerLogged = true;
                        progress?.Report(new DatabaseToolsProgress(count, null, details.ProviderName));

                        continue;
                    }

                    dbContext ??= GetOrCreateContext();
                    dbContext.ProviderDetails.Add(details);
                    LogProviderDetails(logger, details);
                    count++;
                    progress?.Report(new DatabaseToolsProgress(count, null, details.ProviderName));

                    if (count % BatchSize != 0) { continue; }

                    await dbContext.SaveChangesAsync(cancellationToken);
                    dbContext.ChangeTracker.Clear();
                }

                if (!headerLogged && pendingForHeader.Count > 0)
                {
                    dbContext ??= GetOrCreateContext();
                    var lastName = pendingForHeader[^1].ProviderName;
                    count += pendingForHeader.Count;
                    await FlushHeaderAndBufferAsync(logger, dbContext, pendingForHeader, cancellationToken);
                    progress?.Report(new DatabaseToolsProgress(count, null, lastName));
                }

                if (dbContext is null)
                {
                    logger.User(LogLevel.Warning,
                        new LocalizableText(DatabaseToolsLogKeys.CreateNoProvidersResolved, []));

                    SetFailureSummary(new LocalizableText(DatabaseToolsLogKeys.CreateFailureNoProvidersResolved, []));

                    return DatabaseToolsOutcome.Failed;
                }

                logger.Data(LogLevel.Information, string.Empty);
                logger.User(LogLevel.Information, new LocalizableText(DatabaseToolsLogKeys.CreateSavingDatabase, []));

                await dbContext.SaveChangesAsync(cancellationToken);

                logger.User(LogLevel.Information, new LocalizableText(DatabaseToolsLogKeys.CreateDone, []));

                return DatabaseToolsOutcome.Succeeded;
            }
            catch (OperationCanceledException)
            {
                await CleanupPartialUnlessUnmovedOriginalAsync();
                dbContext = null;

                return DatabaseToolsOutcome.Cancelled;
            }
            catch (RegexMatchTimeoutException)
            {
                logger.User(LogLevel.Error, new LocalizableText(DatabaseToolsLogKeys.CreateRegexTimedOut, []));
                await CleanupPartialUnlessUnmovedOriginalAsync();
                dbContext = null;

                return DatabaseToolsOutcome.Failed;
            }
            catch (Exception ex)
            {
                logger.User(LogLevel.Error, new LocalizableText(DatabaseToolsLogKeys.CreateUnexpectedError, []), ex);
                await CleanupPartialUnlessUnmovedOriginalAsync();
                dbContext = null;

                return DatabaseToolsOutcome.Failed;
            }
            finally
            {
                if (dbContext is not null) { await dbContext.DisposeAsync(); }

                wimImage?.Dispose();
                isoImage?.Dispose();
                vhdxImage?.Dispose();
            }

            async Task CleanupPartialUnlessUnmovedOriginalAsync()
            {
                if (_overwriteBackupTaken && !_overwriteBackupCompleted) { return; }

                await CleanupPartialDatabaseAsync(logger, dbContext, request.TargetPath);
            }
        }

        ProviderDbContext GetOrCreateContext()
        {
            if (request.Overwrite && !_overwriteBackupTaken && File.Exists(request.TargetPath))
            {
                _overwriteBackupTaken = true;
                TakeOverwriteBackup();
                _overwriteBackupCompleted = true;
            }

            return new ProviderDbContext(request.TargetPath, false, logger.Trace);
        }
    }

    internal static OfflineImageKind? ResolveImageKind(CreateDatabaseRequest request) =>
        OfflineImageKindResolver.ResolveFromPath(request.OfflineImagePath, request.ImageKind);

    internal static CreateDatabaseMode SelectMode(CreateDatabaseRequest request) =>
        !string.IsNullOrWhiteSpace(request.OfflineImagePath) ?
            CreateDatabaseMode.OfflineImage :
            request.SourcePath is null ?
                CreateDatabaseMode.Local : CreateDatabaseMode.FileSource;

    internal static bool ValidateOfflineImageRequest(CreateDatabaseRequest request, IOperationLog logger)
    {
        IOperationLog offlineLogger = logger.ForCategory(LogCategories.Offline);

        if (string.IsNullOrWhiteSpace(request.OfflineImagePath))
        {
            if (request.ImageKind is not null)
            {
                offlineLogger.User(LogLevel.Error,
                    new LocalizableText(DatabaseToolsLogKeys.CreateImageKindRequiresOfflineImage, []));

                return false;
            }

            if (request.WimIndex is not null)
            {
                offlineLogger.User(LogLevel.Error,
                    new LocalizableText(DatabaseToolsLogKeys.CreateWimIndexRequiresOfflineImage, []));

                return false;
            }

            return true;
        }

        if (request.SourcePath is not null)
        {
            offlineLogger.User(LogLevel.Error,
                new LocalizableText(DatabaseToolsLogKeys.CreateSourceOrOfflineImage, []));

            return false;
        }

        switch (ResolveImageKind(request))
        {
            case OfflineImageKind.Directory:
                {
                    IOperationLog directoryLogger = logger.ForCategory(LogCategories.OfflineProviders);

                    if (request.WimIndex is not null)
                    {
                        directoryLogger.User(LogLevel.Error,
                            new LocalizableText(DatabaseToolsLogKeys.CreateWimIndexAppliesToWimOrIso, []));

                        return false;
                    }

                    if (Directory.Exists(request.OfflineImagePath)) { return true; }

                    if (File.Exists(request.OfflineImagePath))
                    {
                        directoryLogger.User(LogLevel.Error,
                            new LocalizableText(DatabaseToolsLogKeys.CreateOfflineImagePathIsFile,
                                [request.OfflineImagePath]));
                    }
                    else
                    {
                        directoryLogger.User(LogLevel.Error,
                            new LocalizableText(DatabaseToolsLogKeys.CreateOfflineImageDirectoryNotFound,
                                [request.OfflineImagePath]));
                    }

                    return false;
                }

            case OfflineImageKind.Wim:
                {
                    IOperationLog wimLogger = logger.ForCategory(LogCategories.OfflineWim);

                    if (!File.Exists(request.OfflineImagePath))
                    {
                        wimLogger.User(LogLevel.Error,
                            new LocalizableText(DatabaseToolsLogKeys.CreateWimImageFileNotFound,
                                [request.OfflineImagePath]));

                        return false;
                    }

                    if (!IsWimImageFile(request.OfflineImagePath))
                    {
                        wimLogger.User(LogLevel.Error,
                            new LocalizableText(DatabaseToolsLogKeys.CreateWimKindExpectsWimOrEsd,
                                [request.OfflineImagePath]));

                        return false;
                    }

                    if (request.WimIndex is null)
                    {
                        wimLogger.User(LogLevel.Error,
                            new LocalizableText(DatabaseToolsLogKeys.CreateWimIndexRequiredForWim, []));

                        LogAvailableWimIndices(request.OfflineImagePath, wimLogger);

                        return false;
                    }

                    return true;
                }

            case OfflineImageKind.Iso:
                {
                    IOperationLog isoLogger = logger.ForCategory(LogCategories.OfflineIso);

                    if (!File.Exists(request.OfflineImagePath))
                    {
                        isoLogger.User(LogLevel.Error,
                            new LocalizableText(DatabaseToolsLogKeys.CreateIsoImageFileNotFound,
                                [request.OfflineImagePath]));

                        return false;
                    }

                    if (!IsIsoFile(request.OfflineImagePath))
                    {
                        isoLogger.User(LogLevel.Error,
                            new LocalizableText(DatabaseToolsLogKeys.CreateIsoKindExpectsIso,
                                [request.OfflineImagePath]));

                        return false;
                    }

                    if (request.WimIndex is null)
                    {
                        isoLogger.User(LogLevel.Error,
                            new LocalizableText(DatabaseToolsLogKeys.CreateWimIndexRequiredForIso, []));

                        return false;
                    }

                    return true;
                }

            case OfflineImageKind.Vhdx:
                {
                    IOperationLog vhdxLogger = logger.ForCategory(LogCategories.OfflineVhdx);

                    if (!File.Exists(request.OfflineImagePath))
                    {
                        vhdxLogger.User(LogLevel.Error,
                            new LocalizableText(DatabaseToolsLogKeys.CreateVhdxImageFileNotFound,
                                [request.OfflineImagePath]));

                        return false;
                    }

                    if (!IsVhdxFile(request.OfflineImagePath))
                    {
                        vhdxLogger.User(LogLevel.Error,
                            new LocalizableText(DatabaseToolsLogKeys.CreateVhdxKindExpectsVhdOrVhdx,
                                [request.OfflineImagePath]));

                        return false;
                    }

                    if (request.WimIndex is not null)
                    {
                        vhdxLogger.User(LogLevel.Error,
                            new LocalizableText(DatabaseToolsLogKeys.CreateWimIndexNotForVhdx, []));

                        return false;
                    }

                    return true;
                }

            case null:
                offlineLogger.User(LogLevel.Error,
                    new LocalizableText(DatabaseToolsLogKeys.CreateCouldNotDetermineOfflineImageKind,
                        [request.OfflineImagePath]));

                return false;

            default:
                offlineLogger.User(LogLevel.Error,
                    new LocalizableText(DatabaseToolsLogKeys.CreateOfflineImageKindNotSupported,
                        [ResolveImageKind(request)?.ToString() ?? string.Empty]));

                return false;
        }
    }

    private static DatabaseToolsOutcome HandleIsoMountFailure(
        OfflineIsoMountStatus status,
        string isoPath,
        IOperationLog logger)
    {
        string isoName = Path.GetFileName(isoPath);

        switch (status)
        {
            case OfflineIsoMountStatus.NotAnIso:
                logger.User(LogLevel.Error, new LocalizableText(DatabaseToolsLogKeys.CreateNotReadableIso, [isoPath]));

                break;
            case OfflineIsoMountStatus.NoInstallImage:
                logger.User(LogLevel.Error,
                    new LocalizableText(DatabaseToolsLogKeys.CreateIsoNoInstallImage, [isoName]));

                break;
            default:
                logger.User(LogLevel.Error,
                    new LocalizableText(DatabaseToolsLogKeys.CreateCouldNotMountIso, [isoName]));

                break;
        }

        return DatabaseToolsOutcome.Failed;
    }

    private static DatabaseToolsOutcome HandleVhdxMountFailure(
        OfflineVhdxMountStatus status,
        string vhdxPath,
        IOperationLog logger)
    {
        string vhdxName = Path.GetFileName(vhdxPath);

        switch (status)
        {
            case OfflineVhdxMountStatus.NotAVhdx:
                logger.User(LogLevel.Error,
                    new LocalizableText(DatabaseToolsLogKeys.CreateNotReadableVhdx, [vhdxPath]));

                break;
            case OfflineVhdxMountStatus.NoWindowsVolume:
                logger.User(LogLevel.Error,
                    new LocalizableText(DatabaseToolsLogKeys.CreateVhdxNoWindowsVolume, [vhdxName]));

                break;
            case OfflineVhdxMountStatus.MultipleWindowsVolumes:
                logger.User(LogLevel.Error,
                    new LocalizableText(DatabaseToolsLogKeys.CreateVhdxMultipleWindowsVolumes, [vhdxName]));

                break;
            default:
                logger.User(LogLevel.Error,
                    new LocalizableText(DatabaseToolsLogKeys.CreateCouldNotMountVhdx, [vhdxName]));

                break;
        }

        return DatabaseToolsOutcome.Failed;
    }

    private static DatabaseToolsOutcome HandleWimExtractionFailure(
        OfflineWimExtractStatus status,
        string wimPath,
        int wimIndex,
        IOperationLog logger)
    {
        string wimName = Path.GetFileName(wimPath);

        switch (status)
        {
            case OfflineWimExtractStatus.Cancelled:
                return DatabaseToolsOutcome.Cancelled;
            case OfflineWimExtractStatus.NeedsElevation:
                logger.User(LogLevel.Error,
                    new LocalizableText(DatabaseToolsLogKeys.CreateWimExtractionNeedsElevation, [wimName]));

                break;
            case OfflineWimExtractStatus.IndexOutOfRange:
                logger.User(LogLevel.Error,
                    new LocalizableText(DatabaseToolsLogKeys.CreateWimIndexOutOfRange, [wimIndex.ToString(), wimName]));

                LogAvailableWimIndices(wimPath, logger);

                break;
            case OfflineWimExtractStatus.InsufficientSpace:
                logger.User(LogLevel.Error,
                    new LocalizableText(DatabaseToolsLogKeys.CreateWimInsufficientSpace,
                        [wimIndex.ToString(), wimName]));

                break;
            case OfflineWimExtractStatus.NotAWim:
                logger.User(LogLevel.Error, new LocalizableText(DatabaseToolsLogKeys.CreateNotReadableWim, [wimPath]));

                break;
            default:
                logger.User(LogLevel.Error,
                    new LocalizableText(DatabaseToolsLogKeys.CreateCouldNotExtractWim, [wimIndex.ToString(), wimName]));

                break;
        }

        return DatabaseToolsOutcome.Failed;
    }

    private static bool IsIsoFile(string path) =>
        string.Equals(Path.GetExtension(path), ".iso", StringComparison.OrdinalIgnoreCase);

    private static bool IsVhdxFile(string path)
    {
        string extension = Path.GetExtension(path);

        return string.Equals(extension, ".vhdx", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".vhd", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWimImageFile(string path)
    {
        string extension = Path.GetExtension(path);

        return string.Equals(extension, ".wim", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".esd", StringComparison.OrdinalIgnoreCase);
    }

    private static void LogAvailableWimIndices(string wimPath, IOperationLog logger)
    {
        WimImageList imageList = OfflineWimImage.ReadIndexList(wimPath, logger.Trace);

        if (imageList.Status != WimImageListStatus.Ok || imageList.Images.Count == 0) { return; }

        foreach (WimImageEntry image in imageList.Images)
        {
            logger.Data(LogLevel.Information, $"  --wim-index {image.Index}  {image.Name} ({image.Edition})");
        }
    }

    private static void LogWritableProbeFailure(IOperationLog logger, OfflineWriteProbeResult probe, LocalizableText summary)
    {
        if (probe.IoException is not null)
        {
            logger.User(LogLevel.Error, summary, probe.IoException);

            return;
        }

        logger.User(LogLevel.Error, summary);
    }

    private static LocalizableText MapWritableProbeFailure(OfflineWriteProbeResult probe)
    {
        if (probe.Status == OfflineWriteProbeStatus.ControlledFolderAccessBlocked)
        {
            var executable = Path.GetFileName(Environment.ProcessPath ?? "EventLogExpert");

            return new LocalizableText(DatabaseToolsLogKeys.CreateCannotWriteControlledFolderAccess,
                [probe.Directory, executable]);
        }

        return new LocalizableText(DatabaseToolsLogKeys.CreateCannotWriteIo, [probe.Directory]);
    }

    private void DeleteOverwriteBackups(IOperationLog logger)
    {
        foreach (var suffix in s_databaseFileSuffixes)
        {
            var backup = request.TargetPath + suffix + ".bak";

            if (!File.Exists(backup)) { continue; }

            try { File.Delete(backup); }
            catch (Exception ex)
            {
                logger.User(LogLevel.Warning,
                    new LocalizableText(DatabaseToolsLogKeys.CreateDeleteOverwriteBackupFailed,
                        [backup, request.TargetPath]),
                    ex);
            }
        }
    }

    private async Task FlushHeaderAndBufferAsync(
        IOperationLog logger,
        ProviderDbContext dbContext,
        List<ProviderDetails> buffer,
        CancellationToken cancellationToken)
    {
        LogProviderDetailHeader(logger, buffer.Select(p => p.ProviderName));

        foreach (var details in buffer)
        {
            dbContext.ProviderDetails.Add(details);
            LogProviderDetails(logger, details);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
        buffer.Clear();
    }

    private void RestoreOverwriteBackups(IOperationLog logger)
    {
        var mainBackup = request.TargetPath + ".bak";

        try
        {
            SqliteConnection.ClearAllPools();

            if (_overwriteBackupCompleted)
            {
                foreach (var suffix in s_databaseFileSuffixes)
                {
                    var newFile = request.TargetPath + suffix;

                    if (suffix.Length == 0 && !File.Exists(mainBackup)) { continue; }

                    if (File.Exists(newFile)) { File.Delete(newFile); }
                }
            }

            foreach (var suffix in s_databaseFileSuffixes)
            {
                var backup = request.TargetPath + suffix + ".bak";

                if (File.Exists(backup)) { File.Move(backup, request.TargetPath + suffix); }
            }

            logger.User(LogLevel.Information,
                new LocalizableText(DatabaseToolsLogKeys.CreateExistingDatabasePreserved, []));
        }
        catch (Exception ex)
        {
            logger.User(LogLevel.Error,
                new LocalizableText(DatabaseToolsLogKeys.CreateRestoreOriginalDatabaseFailed,
                    [mainBackup, request.TargetPath]),
                ex);
        }
    }

    private void TakeOverwriteBackup()
    {
        foreach (var suffix in s_databaseFileSuffixes)
        {
            var source = request.TargetPath + suffix;

            if (File.Exists(source)) { File.Move(source, source + ".bak"); }
        }
    }

#if DEBUG
    private static void AssertContentEquivalent(ProviderDetails first, ProviderDetails duplicate)
    {
        if (ContentEquivalent(first, duplicate)) { return; }

        throw new InvalidOperationException(
            $"Provider '{duplicate.ProviderName}' produced two rows sharing VersionKey '{duplicate.VersionKey}' that " +
            $"are not content-equivalent. The content hash and {nameof(ProviderContentMerge)} have drifted - a field " +
            $"is hashed for identity but not compared for equivalence (or vice versa).");
    }

    private static bool ContentEquivalent(ProviderDetails first, ProviderDetails duplicate) =>
        ModelsEquivalent(first.Events,
            duplicate.Events,
            static model => ProviderContentMerge.IdentityOf(model),
            ProviderContentMerge.EventsAreEquivalent) &&
        ModelsEquivalent(first.Messages,
            duplicate.Messages,
            static model => ProviderContentMerge.IdentityOf(model),
            ProviderContentMerge.MessagesAreEquivalent) &&
        ModelsEquivalent(first.Parameters,
            duplicate.Parameters,
            static model => ProviderContentMerge.IdentityOf(model),
            ProviderContentMerge.MessagesAreEquivalent) &&
        MapsEquivalent(first.Maps, duplicate.Maps) &&
        DictionaryEqual(first.Keywords, duplicate.Keywords) &&
        DictionaryEqual(first.Opcodes, duplicate.Opcodes) &&
        DictionaryEqual(first.Tasks, duplicate.Tasks) &&
        string.Equals(
            first.ResolvedFromOwningPublisher ?? string.Empty,
            duplicate.ResolvedFromOwningPublisher ?? string.Empty,
            StringComparison.Ordinal);

    private static bool DictionaryEqual<TKey>(IDictionary<TKey, string> first, IDictionary<TKey, string> second)
        where TKey : notnull
    {
        if (first.Count != second.Count) { return false; }

        foreach ((TKey key, string value) in first)
        {
            if (!second.TryGetValue(key, out string? other) || !string.Equals(value, other, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MapsEquivalent(
        IReadOnlyDictionary<string, ValueMapDefinition> first,
        IReadOnlyDictionary<string, ValueMapDefinition> second)
    {
        if (first.Count != second.Count) { return false; }

        foreach ((string key, ValueMapDefinition map) in first)
        {
            if (!second.TryGetValue(key, out ValueMapDefinition? other) ||
                !ProviderContentMerge.MapsAreEquivalent(map, other))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ModelsEquivalent<TModel, TIdentity>(
        IReadOnlyList<TModel> first,
        IReadOnlyList<TModel> second,
        Func<TModel, TIdentity> identityOf,
        Func<TModel, TModel, bool> areEquivalent)
        where TIdentity : notnull
    {
        var firstByIdentity = new Dictionary<TIdentity, TModel>(first.Count);

        foreach (TModel model in first) { firstByIdentity[identityOf(model)] = model; }

        var secondByIdentity = new Dictionary<TIdentity, TModel>(second.Count);

        foreach (TModel model in second) { secondByIdentity[identityOf(model)] = model; }

        if (firstByIdentity.Count != secondByIdentity.Count) { return false; }

        foreach ((TIdentity identity, TModel model) in firstByIdentity)
        {
            if (!secondByIdentity.TryGetValue(identity, out TModel? other) || !areEquivalent(model, other))
            {
                return false;
            }
        }

        return true;
    }
#endif
}
