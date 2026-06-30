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

/// <summary>
///     Creates a new provider database (.db). When the request's SourcePath is null/empty, local providers on this
///     machine are used. When supplied, ONLY the source is used (no fallback to local providers). Streams provider details
///     into the DbContext in batches; defers DbContext creation until at least one provider is resolved so a failed scan
///     does not leave an empty .db on disk.
/// </summary>
internal sealed class CreateDatabaseOperation(CreateDatabaseRequest request) : OperationBase, IDatabaseToolsOperation
{
    private const int BatchSize = 100;

    // The three on-disk files an at-rest SQLite database can occupy: the main file plus its WAL/SHM sidecars. The
    // overwrite backup/restore moves and cleans all three so a restored database is never paired with the aborted
    // rebuild's stale WAL (which would corrupt it). At-rest, cleanly-closed databases normally have only "".
    private static readonly string[] s_databaseFileSuffixes = ["", "-wal", "-shm"];

    // Set only AFTER TakeOverwriteBackup() moves ALL of the old database's files aside. Gates the restore's leftover
    // deletion: if the backup tore midway the new build never started, so the files still at the target paths are
    // unmoved originals and must NOT be deleted (deleting an original -wal/-shm would strip uncheckpointed data).
    private bool _overwriteBackupCompleted;

    // Set the first time the old database is moved aside to ".bak" (overwrite of an existing target, at the first
    // ProviderDbContext creation). Drives the post-run finalize: delete the backup on success, restore it otherwise.
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

        // Unconditional stale-backup preflight (mirrors DestructiveRecovery.WrapUpgradeAsync). An interrupted prior
        // overwrite can leave a ".bak" snapshot behind - possibly with the target itself already gone. Proceeding would
        // let a later success delete that snapshot (the sole surviving copy) or break the next overwrite's File.Move.
        // Fire whenever ANY backup file (main or WAL/SHM sidecar) exists, regardless of whether the target exists.
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

        // Pre-flight the chosen .db destination so a Controlled Folder Access / ACL denial fails fast with an actionable
        // message instead of surfacing later as an opaque write error after a long scan/extract.
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

        // Defensive recompile if input has Regex.InfiniteMatchTimeout (otherwise catch below is dead).
        var filterRegex = EnsureBoundedTimeout(request.FilterRegex, TimeSpan.FromSeconds(5));

        var outcome = await CreateCoreAsync();

        // Overwrite finalize: the old database was moved aside to ".bak" only if a new database was actually started
        // (first ProviderDbContext creation, in GetOrCreateContext). Keep the new one and drop the snapshot on success;
        // otherwise restore the snapshot so a failed/cancelled rebuild never destroys the prior database.
        if (!_overwriteBackupTaken) { return outcome; }

        if (outcome is DatabaseToolsOutcome.Succeeded) { DeleteOverwriteBackups(logger); }
        else { RestoreOverwriteBackups(logger); }

        return outcome;

        async Task<DatabaseToolsOutcome> CreateCoreAsync()
        {
        var count = 0;
        var headerLogged = false;
        var pendingForHeader = new List<ProviderDetails>(BatchSize);

        // Collapse identical content arriving under different source keys (e.g. an unstamped legacy row plus an
        // already-hashed row in a multi-file source): both re-hash to the same VersionKey, so the second would
        // otherwise collide on the composite primary key. Track stamped identities and skip duplicates first-wins.
        var stampedIdentities = new HashSet<ProviderIdentity>();
#if DEBUG
        // CI-only tripwire: fail the build if a (Name, VersionKey) collision's rows are not content-equivalent (hash and
        // merge drift). No release retention.
        var firstByIdentity = new Dictionary<ProviderIdentity, ProviderDetails>();
#endif

        // Defer creating the DbContext (and therefore the .db file on disk) until we have
        // at least one provider to persist. This prevents leaving an empty database behind
        // when no provider details could be resolved.
        ProviderDbContext? dbContext = null;
        OfflineWimImage? wimImage = null;
        OfflineIsoImage? isoImage = null;
        OfflineVhdxImage? vhdxImage = null;

        try
        {
            var mode = SelectMode(request);

            // A WIM/ISO image is extracted to a temp folder, then read like a mounted volume. ISO just resolves the inner
            // install.wim first. A failed mount/extraction surfaces a specific, actionable error and leaves no .db behind.
            string? effectiveOfflineImagePath = request.OfflineImagePath;
            OfflineImageKind? kind = mode == CreateDatabaseMode.OfflineImage ? ResolveImageKind(request) : null;

            // A WIM/ISO build extracts into the scratch root via a long native WIMApplyImage that ignores the cooperative
            // cancel; pre-flight the scratch root so a CFA/ACL denial fails fast here instead of wedging the apply.
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

            // A VHD/VHDX is attached read-only, its Windows partition resolved, then read like a mounted volume - no WIM
            // extraction and no image index. A failed mount surfaces a specific, actionable error and leaves no .db behind.
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

            // ONE switch picks BOTH the provider stream AND the provenance so the two cannot desync: an offline image
            // build must NOT read host provenance (the facade already stamped each row with the IMAGE's OS, and a host
            // read here would overwrite it); host provenance is read ONLY for a local build. The bounded filterRegex
            // (not request.FilterRegex) reaches every source so the RegexMatchTimeoutException catch stays reachable.
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

                // Stamp the content hash so distinct versions of a provider name coexist under the composite key and
                // identical providers (across machines / OS builds) collapse to one row. Idempotent for an
                // already-hashed source; computes the key for freshly-resolved (live) providers.
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
            // Any non-cancellation, non-regex-timeout failure (e.g., EF/SQLite errors mid-save) - no stub .db.
            logger.Error($"Unexpected error creating database: {ex.Message}");
            await CleanupPartialUnlessUnmovedOriginalAsync();
            dbContext = null;

            return DatabaseToolsOutcome.Failed;
        }
        finally
        {
            if (dbContext is not null) { await dbContext.DisposeAsync(); }

            // Delete the extracted WIM temp AFTER persistence completes (the final SaveChangesAsync reads from it), then
            // detach the ISO/VHDX (the mounts are no longer needed once providers are persisted).
            wimImage?.Dispose();
            isoImage?.Dispose();
            vhdxImage?.Dispose();
        }

        // Generic partial-cleanup deletes the target path as a disposable stub. That is correct for a non-overwrite run
        // or once the overwrite backup has fully completed (the target is then a partial NEW build). But if the backup
        // started and tore before completing, the file still at the target is the UNMOVED ORIGINAL - deleting it would
        // destroy the prior database. In that case skip the delete and let RestoreOverwriteBackups move any ".bak" back.
        async Task CleanupPartialUnlessUnmovedOriginalAsync()
        {
            if (_overwriteBackupTaken && !_overwriteBackupCompleted) { return; }

            await CleanupPartialDatabaseAsync(logger, dbContext, request.TargetPath);
        }
        }

        // First ProviderDbContext creation is where the new database file appears on disk. If this is an overwrite of an
        // existing target, move the old database (and any WAL/SHM sidecars) aside to ".bak" exactly once, BEFORE
        // constructing the writable context (which calls Database.EnsureCreated() and would otherwise open/overwrite the
        // live file). ALL three creation sites route through here so the header-flush path cannot bypass the backup.
        ProviderDbContext GetOrCreateContext()
        {
            if (request.Overwrite && !_overwriteBackupTaken && File.Exists(request.TargetPath))
            {
                // Set the flag BEFORE the move so a partially-completed backup still triggers the restore path, which
                // heals a torn move by restoring whichever ".bak" files were created.
                _overwriteBackupTaken = true;
                TakeOverwriteBackup();
                _overwriteBackupCompleted = true;
            }

            return new ProviderDbContext(request.TargetPath, false, logger);
        }
    }

    // The effective kind is the explicit --image-kind when given, otherwise inferred from the path: an existing directory
    // is a mounted volume / extracted folder; a .wim/.esd is a WIM; a .iso is an ISO. Null = neither given nor inferable.
    // Path-based resolution is shared with the elevation helper via OfflineImageKindResolver.
    internal static OfflineImageKind? ResolveImageKind(CreateDatabaseRequest request) =>
        OfflineImageKindResolver.ResolveFromPath(request.OfflineImagePath, request.ImageKind);

    /// <summary>
    ///     Picks the provider source for the request. An offline image (a non-whitespace <c>OfflineImagePath</c>) wins;
    ///     otherwise a null <c>SourcePath</c> means local providers and a non-null one means a file source. Pure so the mode
    ///     selection (and the host-provenance suppression keyed on it) can be unit-tested without a real image.
    /// </summary>
    internal static CreateDatabaseMode SelectMode(CreateDatabaseRequest request) =>
        !string.IsNullOrWhiteSpace(request.OfflineImagePath) ? CreateDatabaseMode.OfflineImage
        : request.SourcePath is null ? CreateDatabaseMode.Local
        : CreateDatabaseMode.FileSource;

    internal static bool ValidateOfflineImageRequest(CreateDatabaseRequest request, ITraceLogger logger)
    {
        if (string.IsNullOrWhiteSpace(request.OfflineImagePath))
        {
            // No offline image: reject orphan WIM options so they are never silently ignored (which would build from the
            // wrong source). Kind is auto-detected, so an EXPLICIT kind or a stray index without an image is the error.
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

                // The index range and elevation are validated by the extraction itself (so the messages list the actual
                // images and the elevation prompt comes only when an apply is really needed). A MISSING index, though, is
                // a request-shape error - list the choices so the user can pick one.
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

                // An ISO's install.wim is multi-edition, so an index is required - but its choices cannot be listed without
                // mounting, which the validator must not do. The extraction (after mount) reports a bad index with choices.
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

                // A VHD/VHDX is read directly from its Windows partition; an image index does not apply, so a stray one is
                // a request-shape error rather than something to silently ignore.
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

    /// The extraction already cleaned up any partial temp, so there is nothing to dispose here.
    /// </summary>
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

    // Drops the ".bak" snapshot (and any sidecar snapshots) after a successful overwrite. Best-effort: a leftover backup
    // is harmless (the new database is intact) but would block the next overwrite, so warn if it cannot be removed.
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

    // Restores the ".bak" snapshot after a failed/cancelled overwrite. First clears the connection pool and removes any
    // new-build leftovers (the partial main file plus its orphaned WAL/SHM sidecars, which CleanupPartialDatabaseAsync
    // does NOT delete) so the restored database is never paired with the aborted build's stale WAL, then moves each
    // ".bak" back. Best-effort: on failure the ".bak" is left in place with an actionable log line for manual recovery.
    private void RestoreOverwriteBackups(ITraceLogger logger)
    {
        var mainBackup = request.TargetPath + ".bak";

        try
        {
            SqliteConnection.ClearAllPools();

            // Only delete files if the backup fully completed (meaning the new database was started). If it tore midway,
            // the new database never started, so any files present are unmoved originals and must NOT be deleted.
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

    // Moves the existing database and any WAL/SHM sidecars to their ".bak" names. The stale-backup preflight already
    // guaranteed no ".bak" exists, so each File.Move targets a free path. Throws on failure (e.g., a locked file); the
    // caller's surrounding try/catch then cleans up and the overwrite finalize restores whatever was moved.
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
        // Compare DISTINCT identities both ways (the hash drops exact-duplicate rows, so raw counts can differ).
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
