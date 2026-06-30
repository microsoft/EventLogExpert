// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.Eventing.PublisherMetadata;
using EventLogExpert.Eventing.PublisherMetadata.Offline;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Resolution;
using EventLogExpert.ProviderDatabase.Context;
using EventLogExpert.ProviderDatabase.Hashing;
using Microsoft.Data.Sqlite;
using System.Text.RegularExpressions;

namespace EventLogExpert.DatabaseTools.CreateDatabase;

internal sealed class CreateDatabaseOperation(CreateDatabaseRequest request) : OperationBase, IDatabaseToolsOperation
{
    private const int BatchSize = 100;

    // SQLite at rest may include main, WAL, and SHM files; overwrite backup/restore must move all three together.
    private static readonly string[] s_databaseFileSuffixes = ["", "-wal", "-shm"];

    // Set only after every sidecar backup moves; restore must not delete unmoved originals after a torn backup.
    private bool _overwriteBackupCompleted;

    private bool _overwriteBackupTaken;

    internal enum CreateDatabaseMode { Local, FileSource, OfflineImage }

    public async Task<DatabaseToolsOutcome> ExecuteAsync(
        ITraceLogger logger,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(Path.GetExtension(request.TargetPath), ".db", StringComparison.OrdinalIgnoreCase))
        {
            logger.Error($"File extension must be .db.");

            return DatabaseToolsOutcome.Failed;
        }

        // Any stale .bak sidecar may be the sole surviving copy after an interrupted overwrite and must block retry.
        foreach (var suffix in s_databaseFileSuffixes)
        {
            var backupPath = request.TargetPath + suffix + ".bak";

            if (File.Exists(backupPath))
            {
                logger.Error($"A recovery backup from an interrupted overwrite already exists at {backupPath}. Inspect, rename, or delete it before retrying so the snapshot is not overwritten.");

                return DatabaseToolsOutcome.Failed;
            }
        }

        if (File.Exists(request.TargetPath) && !request.Overwrite)
        {
            logger.Error($"Cannot create database because file already exists: {request.TargetPath}");

            return DatabaseToolsOutcome.Failed;
        }

        if (!ValidateOfflineImageRequest(request, logger) ||
            (request.SourcePath is not null && !ProviderSource.TryValidate(request.SourcePath, logger)))
        {
            return DatabaseToolsOutcome.Failed;
        }

        // Fail fast on destination ACL/CFA denial before expensive scan or extraction work begins.
        string targetDirectory = Path.GetDirectoryName(Path.GetFullPath(request.TargetPath)) ?? request.TargetPath;
        string? targetBlocked = OfflineScratch.ProbeWritable(targetDirectory);

        if (targetBlocked is not null)
        {
            logger.Error($"{targetBlocked}");
            SetFailureSummary(targetBlocked);

            return DatabaseToolsOutcome.Failed;
        }

        HashSet<string> excludeProviderNames = new(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(request.SkipProvidersInFile))
        {
            if (!ProviderSource.TryValidate(request.SkipProvidersInFile, logger))
            {
                return DatabaseToolsOutcome.Failed;
            }

            foreach (var name in await ProviderSource.LoadProviderNamesAsync(request.SkipProvidersInFile, logger, cancellationToken: cancellationToken))
            {
                excludeProviderNames.Add(name);
            }

            logger.Information($"Found {excludeProviderNames.Count} providers in {request.SkipProvidersInFile}. These will not be included in the new database.");
        }

        var filterRegex = EnsureBoundedTimeout(request.FilterRegex, TimeSpan.FromSeconds(5));

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

        // Create DbContext only after the first provider so failed scans leave no empty database.
        ProviderDbContext? dbContext = null;
        OfflineWimImage? wimImage = null;
        OfflineIsoImage? isoImage = null;
        OfflineVhdxImage? vhdxImage = null;

        try
        {
            var mode = SelectMode(request);

            string? effectiveOfflineImagePath = request.OfflineImagePath;
            OfflineImageKind? kind = mode == CreateDatabaseMode.OfflineImage ? ResolveImageKind(request) : null;

            // WIM apply ignores cooperative cancellation on denied writes, so probe scratch ACLs before native extraction.
            if (kind is OfflineImageKind.Wim or OfflineImageKind.Iso)
            {
                string? scratchBlocked = OfflineScratch.ProbeWritable(OfflineScratch.Root);

                if (scratchBlocked is not null)
                {
                    logger.Error($"{scratchBlocked}");
                    SetFailureSummary(scratchBlocked);

                    return DatabaseToolsOutcome.Failed;
                }
            }

            if (kind is OfflineImageKind.Iso)
            {
                OfflineIsoMountResult mount = OfflineIsoImage.TryMount(request.OfflineImagePath!, logger);

                if (mount.Status != OfflineIsoMountStatus.Mounted)
                {
                    return HandleIsoMountFailure(mount.Status, request.OfflineImagePath!, logger);
                }

                isoImage = mount.Image;
            }

            if (kind is OfflineImageKind.Vhdx)
            {
                OfflineVhdxMountResult mount = OfflineVhdxImage.TryMount(request.OfflineImagePath!, logger);

                if (mount.Status != OfflineVhdxMountStatus.Mounted)
                {
                    return HandleVhdxMountFailure(mount.Status, request.OfflineImagePath!, logger);
                }

                vhdxImage = mount.Image;
                effectiveOfflineImagePath = vhdxImage!.VolumeRoot;
            }

            if (kind is OfflineImageKind.Wim or OfflineImageKind.Iso)
            {
                string wimSourcePath = isoImage?.InstallImagePath ?? request.OfflineImagePath!;

                OfflineWimExtractResult extraction = await OfflineWimImage.TryExtractAsync(
                    wimSourcePath, request.WimIndex!.Value, OfflineScratch.Root, logger, cancellationToken);

                if (extraction.Status != OfflineWimExtractStatus.Extracted)
                {
                    return HandleWimExtractionFailure(extraction.Status, wimSourcePath, request.WimIndex!.Value, logger);
                }

                wimImage = extraction.Image;
                effectiveOfflineImagePath = wimImage!.ExtractedRoot;
            }

            // Offline providers already carry image provenance; only local builds read host provenance.
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

                    sourceOsProvenance = SourceOsProvenance.Read(logger);

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
                logger.Warning($"No provider details could be resolved from the source. Database was not created.");
                SetFailureSummary("No providers could be resolved from the source, so no database was created.");

                return DatabaseToolsOutcome.Failed;
            }

            logger.Information($"");
            logger.Information($"Saving database. Please wait...");

            await dbContext.SaveChangesAsync(cancellationToken);

            logger.Information($"Done!");

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
            logger.Error($"The provider-name regex timed out. The pattern may cause catastrophic backtracking.");
            await CleanupPartialUnlessUnmovedOriginalAsync();
            dbContext = null;

            return DatabaseToolsOutcome.Failed;
        }
        catch (Exception ex)
        {
            logger.Error($"Unexpected error creating database: {ex.Message}");
            await CleanupPartialUnlessUnmovedOriginalAsync();
            dbContext = null;

            return DatabaseToolsOutcome.Failed;
        }
        finally
        {
            if (dbContext is not null) { await dbContext.DisposeAsync(); }

            // Dispose extracted WIM after SaveChanges because persisted rows may still read from it.
            wimImage?.Dispose();
            isoImage?.Dispose();
            vhdxImage?.Dispose();
        }

        // Never delete target files after a torn overwrite backup; they may be unmoved originals.
        async Task CleanupPartialUnlessUnmovedOriginalAsync()
        {
            if (_overwriteBackupTaken && !_overwriteBackupCompleted) { return; }

            await CleanupPartialDatabaseAsync(logger, dbContext, request.TargetPath);
        }
        }

        // Back up the old database before the writable context opens or creates target files.
        ProviderDbContext GetOrCreateContext()
        {
            if (request.Overwrite && !_overwriteBackupTaken && File.Exists(request.TargetPath))
            {
                // Set before moving so a torn backup still enters restore.
                _overwriteBackupTaken = true;
                TakeOverwriteBackup();
                _overwriteBackupCompleted = true;
            }

            return new ProviderDbContext(request.TargetPath, false, logger);
        }
    }

    internal static OfflineImageKind? ResolveImageKind(CreateDatabaseRequest request) =>
        OfflineImageKindResolver.ResolveFromPath(request.OfflineImagePath, request.ImageKind);

    internal static CreateDatabaseMode SelectMode(CreateDatabaseRequest request) =>
        !string.IsNullOrWhiteSpace(request.OfflineImagePath) ? CreateDatabaseMode.OfflineImage
        : request.SourcePath is null ? CreateDatabaseMode.Local
        : CreateDatabaseMode.FileSource;

    internal static bool ValidateOfflineImageRequest(CreateDatabaseRequest request, ITraceLogger logger)
    {
        // Reject orphan WIM options so the command cannot silently fall back to local providers.
        if (string.IsNullOrWhiteSpace(request.OfflineImagePath))
        {
            if (request.ImageKind is not null)
            {
                logger.Error($"--image-kind requires an offline image (--offline-image).");

                return false;
            }

            if (request.WimIndex is not null)
            {
                logger.Error($"--wim-index requires an offline image (--offline-image) pointing at a .wim/.esd file.");

                return false;
            }

            return true;
        }

        if (request.SourcePath is not null)
        {
            logger.Error($"Specify a source OR an offline image, not both.");

            return false;
        }

        switch (ResolveImageKind(request))
        {
            case OfflineImageKind.Directory:
                if (request.WimIndex is not null)
                {
                    logger.Error($"--wim-index applies only to --image-kind wim or iso.");

                    return false;
                }

                if (Directory.Exists(request.OfflineImagePath)) { return true; }

                if (File.Exists(request.OfflineImagePath))
                {
                    logger.Error($"Offline image path is a file, not a directory: {request.OfflineImagePath}. For a .wim/.esd file, add --image-kind wim --wim-index N.");
                }
                else
                {
                    logger.Error($"Offline image directory not found: {request.OfflineImagePath}");
                }

                return false;

            case OfflineImageKind.Wim:
                if (!File.Exists(request.OfflineImagePath))
                {
                    logger.Error($"WIM image file not found: {request.OfflineImagePath}");

                    return false;
                }

                if (!IsWimImageFile(request.OfflineImagePath))
                {
                    logger.Error($"--image-kind wim expects a .wim or .esd file: {request.OfflineImagePath}");

                    return false;
                }

                if (request.WimIndex is null)
                {
                    logger.Error($"--wim-index is required for --image-kind wim. Choose an image:");
                    LogAvailableWimIndices(request.OfflineImagePath, logger);

                    return false;
                }

                return true;

            case OfflineImageKind.Iso:
                if (!File.Exists(request.OfflineImagePath))
                {
                    logger.Error($"ISO image file not found: {request.OfflineImagePath}");

                    return false;
                }

                if (!IsIsoFile(request.OfflineImagePath))
                {
                    logger.Error($"--image-kind iso expects a .iso file: {request.OfflineImagePath}");

                    return false;
                }

                // Validator cannot mount ISO just to list choices; extraction reports bad indices after mount.
                if (request.WimIndex is null)
                {
                    logger.Error(
                        $"--wim-index is required for --image-kind iso (sources\\install.wim is a multi-edition image). " +
                        $"Pass --wim-index 1 for the default edition, or extract sources\\install.wim and run --image-kind wim to list editions.");

                    return false;
                }

                return true;

            case OfflineImageKind.Vhdx:
                if (!File.Exists(request.OfflineImagePath))
                {
                    logger.Error($"VHD/VHDX image file not found: {request.OfflineImagePath}");

                    return false;
                }

                if (!IsVhdxFile(request.OfflineImagePath))
                {
                    logger.Error($"--image-kind vhdx expects a .vhdx or .vhd file: {request.OfflineImagePath}");

                    return false;
                }

                if (request.WimIndex is not null)
                {
                    logger.Error($"--wim-index applies only to --image-kind wim or iso, not vhdx.");

                    return false;
                }

                return true;

            case null:
                logger.Error(
                    $"Could not determine the offline image kind for '{request.OfflineImagePath}'. Pass --image-kind " +
                    $"directory or wim (.wim/.esd) or iso (.iso) or vhdx (.vhdx/.vhd), or point at a mounted volume / extracted image folder.");

                return false;

            default:
                logger.Error($"Offline image kind {ResolveImageKind(request)} is not supported.");

                return false;
        }
    }

    private static DatabaseToolsOutcome HandleIsoMountFailure(OfflineIsoMountStatus status, string isoPath, ITraceLogger logger)
    {
        string isoName = Path.GetFileName(isoPath);

        switch (status)
        {
            case OfflineIsoMountStatus.NotAnIso:
                logger.Error($"{isoPath} is not a readable ISO image.");

                break;
            case OfflineIsoMountStatus.NoInstallImage:
                logger.Error($"{isoName} has no sources\\install.wim or install.esd; only a Windows install ISO is supported.");

                break;
            default:
                logger.Error($"Could not mount {isoName} (if this is an access-denied error, re-run elevated).");

                break;
        }

        return DatabaseToolsOutcome.Failed;
    }

    private static DatabaseToolsOutcome HandleVhdxMountFailure(OfflineVhdxMountStatus status, string vhdxPath, ITraceLogger logger)
    {
        string vhdxName = Path.GetFileName(vhdxPath);

        switch (status)
        {
            case OfflineVhdxMountStatus.NotAVhdx:
                logger.Error($"{vhdxPath} is not a readable VHD/VHDX image.");

                break;
            case OfflineVhdxMountStatus.NoWindowsVolume:
                logger.Error($"{vhdxName} has no readable Windows volume (\\Windows\\System32); the disk may be data-only or BitLocker-encrypted.");

                break;
            case OfflineVhdxMountStatus.MultipleWindowsVolumes:
                logger.Error($"{vhdxName} contains more than one Windows volume; selecting one is ambiguous, so no database was created.");

                break;
            default:
                logger.Error($"Could not mount {vhdxName} (if this is an access-denied error, re-run elevated).");

                break;
        }

        return DatabaseToolsOutcome.Failed;
    }

    private static DatabaseToolsOutcome HandleWimExtractionFailure(
        OfflineWimExtractStatus status, string wimPath, int wimIndex, ITraceLogger logger)
    {
        string wimName = Path.GetFileName(wimPath);

        switch (status)
        {
            case OfflineWimExtractStatus.Cancelled:
                return DatabaseToolsOutcome.Cancelled;
            case OfflineWimExtractStatus.NeedsElevation:
                logger.Error($"Extracting an image from {wimName} requires administrator privileges. Re-run elevated.");

                break;
            case OfflineWimExtractStatus.IndexOutOfRange:
                logger.Error($"Image index {wimIndex} is not in {wimName}.");
                LogAvailableWimIndices(wimPath, logger);

                break;
            case OfflineWimExtractStatus.InsufficientSpace:
                logger.Error($"Not enough free disk space to extract image {wimIndex} from {wimName}.");

                break;
            case OfflineWimExtractStatus.NotAWim:
                logger.Error($"{wimPath} is not a readable WIM or ESD image.");

                break;
            default:
                logger.Error($"Could not extract image {wimIndex} from {wimName}.");

                break;
        }

        return DatabaseToolsOutcome.Failed;
    }

    private static bool IsIsoFile(string path) =>
        string.Equals(Path.GetExtension(path), ".iso", StringComparison.OrdinalIgnoreCase);

    private static bool IsVhdxFile(string path)
    {
        string extension = Path.GetExtension(path);

        return string.Equals(extension, ".vhdx", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".vhd", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWimImageFile(string path)
    {
        string extension = Path.GetExtension(path);

        return string.Equals(extension, ".wim", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".esd", StringComparison.OrdinalIgnoreCase);
    }

    private static void LogAvailableWimIndices(string wimPath, ITraceLogger logger)
    {
        WimImageList imageList = OfflineWimImage.ReadIndexList(wimPath, logger);

        if (imageList.Status != WimImageListStatus.Ok || imageList.Images.Count == 0) { return; }

        foreach (WimImageEntry image in imageList.Images)
        {
            logger.Information($"  --wim-index {image.Index}  {image.Name} ({image.Edition})");
        }
    }

    private void DeleteOverwriteBackups(ITraceLogger logger)
    {
        foreach (var suffix in s_databaseFileSuffixes)
        {
            var backup = request.TargetPath + suffix + ".bak";

            if (!File.Exists(backup)) { continue; }

            try { File.Delete(backup); }
            catch (Exception ex)
            {
                logger.Warning($"Could not delete the overwrite backup at {backup}: {ex.Message}. Delete it manually before the next overwrite of {request.TargetPath}.");
            }
        }
    }

    private async Task FlushHeaderAndBufferAsync(
        ITraceLogger logger,
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

    // Clear pools and remove aborted-build sidecars before restoring backups.
    private void RestoreOverwriteBackups(ITraceLogger logger)
    {
        var mainBackup = request.TargetPath + ".bak";

        try
        {
            SqliteConnection.ClearAllPools();

            // Only delete files after a complete backup; otherwise they may be unmoved originals.
            if (_overwriteBackupCompleted)
            {
                foreach (var suffix in s_databaseFileSuffixes)
                {
                    var newFile = request.TargetPath + suffix;

                    // Never delete the main file unless its snapshot exists to take its place.
                    if (suffix.Length == 0 && !File.Exists(mainBackup)) { continue; }

                    if (File.Exists(newFile)) { File.Delete(newFile); }
                }
            }

            foreach (var suffix in s_databaseFileSuffixes)
            {
                var backup = request.TargetPath + suffix + ".bak";

                if (File.Exists(backup)) { File.Move(backup, request.TargetPath + suffix); }
            }

            logger.Information($"Existing database was preserved: restored from backup after the rebuild did not complete.");
        }
        catch (Exception ex)
        {
            logger.Error($"Could not restore the original database from backup ({ex.GetType().Name}: {ex.Message}). The backup remains at {mainBackup}; rename it back to {request.TargetPath} to recover.");
        }
    }

    // Stale-backup preflight guarantees every .bak move targets a free path.
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
            if (!second.TryGetValue(key, out ValueMapDefinition? other) || !ProviderContentMerge.MapsAreEquivalent(map, other))
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
        // Compare distinct identities both ways because the hash drops exact duplicate rows.
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
