// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.Eventing.PublisherMetadata;
using EventLogExpert.Eventing.PublisherMetadata.Offline;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Resolution;
using EventLogExpert.ProviderDatabase.Context;
using EventLogExpert.ProviderDatabase.Hashing;
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

    internal enum CreateDatabaseMode { Local, FileSource, OfflineImage }

    public async Task<DatabaseToolsOutcome> ExecuteAsync(
        ITraceLogger logger,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (File.Exists(request.TargetPath))
        {
            logger.Error($"Cannot create database because file already exists: {request.TargetPath}");

            return DatabaseToolsOutcome.Failed;
        }

        if (!string.Equals(Path.GetExtension(request.TargetPath), ".db", StringComparison.OrdinalIgnoreCase))
        {
            logger.Error($"File extension must be .db.");

            return DatabaseToolsOutcome.Failed;
        }

        if (!ValidateOfflineImageRequest(request, logger))
        {
            return DatabaseToolsOutcome.Failed;
        }

        if (request.SourcePath is not null && !ProviderSource.TryValidate(request.SourcePath, logger))
        {
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

        // A WIM image is extracted to a temp folder up front; the OfflineWimImage owns that ~GB-scale temp and is disposed
        // in the finally (AFTER the final SaveChangesAsync, which materializes lazy DLL reads from the extracted tree).
        OfflineWimImage? wimImage = null;

        try
        {
            var mode = SelectMode(request);

            // For a WIM image, extract the requested index to a temp folder, then read providers from that folder exactly
            // like a mounted volume. A failed extraction surfaces a specific, actionable error and leaves no .db behind.
            string? effectiveOfflineImagePath = request.OfflineImagePath;

            if (mode == CreateDatabaseMode.OfflineImage && ResolveImageKind(request) == OfflineImageKind.Wim)
            {
                OfflineWimExtractResult extraction = await OfflineWimImage.TryExtractAsync(
                    request.OfflineImagePath!, request.WimIndex!.Value, Path.GetTempPath(), logger, cancellationToken);

                if (extraction.Status != OfflineWimExtractStatus.Extracted)
                {
                    return HandleWimExtractionFailure(extraction.Status, request.OfflineImagePath!, request.WimIndex!.Value, logger);
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

                    dbContext ??= new ProviderDbContext(request.TargetPath, false, logger);
                    count += pendingForHeader.Count;
                    await FlushHeaderAndBufferAsync(logger, dbContext, pendingForHeader, cancellationToken);
                    headerLogged = true;
                    progress?.Report(new DatabaseToolsProgress(count, null, details.ProviderName));

                    continue;
                }

                dbContext ??= new ProviderDbContext(request.TargetPath, false, logger);
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
                dbContext ??= new ProviderDbContext(request.TargetPath, false, logger);
                var lastName = pendingForHeader[^1].ProviderName;
                count += pendingForHeader.Count;
                await FlushHeaderAndBufferAsync(logger, dbContext, pendingForHeader, cancellationToken);
                progress?.Report(new DatabaseToolsProgress(count, null, lastName));
            }

            if (dbContext is null)
            {
                logger.Warning($"No provider details could be resolved from the source. Database was not created.");

                return DatabaseToolsOutcome.Succeeded;
            }

            logger.Information($"");
            logger.Information($"Saving database. Please wait...");

            await dbContext.SaveChangesAsync(cancellationToken);

            logger.Information($"Done!");

            return DatabaseToolsOutcome.Succeeded;
        }
        catch (OperationCanceledException)
        {
            await CleanupPartialDatabaseAsync(logger, dbContext, request.TargetPath);
            dbContext = null;

            return DatabaseToolsOutcome.Cancelled;
        }
        catch (RegexMatchTimeoutException)
        {
            logger.Error($"The provider-name regex timed out. The pattern may cause catastrophic backtracking.");
            await CleanupPartialDatabaseAsync(logger, dbContext, request.TargetPath);
            dbContext = null;

            return DatabaseToolsOutcome.Failed;
        }
        catch (Exception ex)
        {
            // Any non-cancellation, non-regex-timeout failure (e.g., EF/SQLite errors mid-save) - no stub .db.
            logger.Error($"Unexpected error creating database: {ex.Message}");
            await CleanupPartialDatabaseAsync(logger, dbContext, request.TargetPath);
            dbContext = null;

            return DatabaseToolsOutcome.Failed;
        }
        finally
        {
            if (dbContext is not null) { await dbContext.DisposeAsync(); }

            // Delete the extracted WIM temp AFTER persistence completes (the final SaveChangesAsync reads from it).
            wimImage?.Dispose();
        }
    }

    // The effective kind is the explicit --image-kind when given, otherwise inferred from the path: an existing directory
    // is a mounted volume / extracted folder; a .wim/.esd is a WIM; a .iso is an ISO. Null = neither given nor inferable.
    internal static OfflineImageKind? ResolveImageKind(CreateDatabaseRequest request)
    {
        if (request.ImageKind is { } explicitKind) { return explicitKind; }

        if (string.IsNullOrWhiteSpace(request.OfflineImagePath)) { return null; }

        if (Directory.Exists(request.OfflineImagePath)) { return OfflineImageKind.Directory; }

        string extension = Path.GetExtension(request.OfflineImagePath.TrimEnd('.', ' '));

        return extension.Equals(".wim", StringComparison.OrdinalIgnoreCase) || extension.Equals(".esd", StringComparison.OrdinalIgnoreCase)
            ? OfflineImageKind.Wim
            : extension.Equals(".iso", StringComparison.OrdinalIgnoreCase) ? OfflineImageKind.Iso : null;
    }

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
                    logger.Error($"--wim-index applies only to --image-kind wim.");

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

            case null:
                logger.Error(
                    $"Could not determine the offline image kind for '{request.OfflineImagePath}'. Pass --image-kind " +
                    $"directory or wim (.wim/.esd), or point at a mounted volume / extracted image folder.");

                return false;

            default:
                logger.Error(
                    $"Offline ISO images are not yet supported; use a mounted volume or extracted image folder " +
                    $"(--image-kind directory) or a .wim/.esd file (--image-kind wim).");

                return false;
        }
    }

    /// <summary>
    ///     Maps a non-success <see cref="OfflineWimExtractStatus" /> to an actionable error and the operation outcome.
    ///     The extraction already cleaned up any partial temp, so there is nothing to dispose here.
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
        ModelsEquivalent(first.Events, duplicate.Events, static model => ProviderContentMerge.IdentityOf(model), ProviderContentMerge.EventsAreEquivalent) &&
        ModelsEquivalent(first.Messages, duplicate.Messages, static model => ProviderContentMerge.IdentityOf(model), ProviderContentMerge.MessagesAreEquivalent) &&
        ModelsEquivalent(first.Parameters, duplicate.Parameters, static model => ProviderContentMerge.IdentityOf(model), ProviderContentMerge.MessagesAreEquivalent) &&
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
