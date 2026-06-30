// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Resolution;
using EventLogExpert.Provider.Schema;
using EventLogExpert.ProviderDatabase.Context;
using Microsoft.EntityFrameworkCore;
using System.Data.Common;

namespace EventLogExpert.DatabaseTools.MergeDatabase;

internal sealed class MergeDatabaseOperation(MergeDatabaseRequest request) : OperationBase, IDatabaseToolsOperation
{
    private const int BatchSize = 100;

    public async Task<DatabaseToolsOutcome> ExecuteAsync(
        ITraceLogger logger,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!ProviderSource.TryValidate(request.SourcePath, logger) ||
            !await ProviderSource.ValidateSourceSchemasAsync(request.SourcePath, logger, cancellationToken))
        {
            return DatabaseToolsOutcome.Failed;
        }

        if (!File.Exists(request.TargetDatabasePath))
        {
            logger.Error($"File not found: {request.TargetDatabasePath}");

            return DatabaseToolsOutcome.Failed;
        }

        var sourceIdentities = (await ProviderSource.LoadProviderIdentitiesAsync(
            request.SourcePath, logger, cancellationToken: cancellationToken)).ToHashSet();

        if (sourceIdentities.Count == 0)
        {
            logger.Warning($"No providers were discovered in the source.");
            SetFailureSummary("No providers were discovered in the source, so the database was not modified.");

            return DatabaseToolsOutcome.Failed;
        }

        var sourceNames = sourceIdentities
            .Select(identity => identity.ProviderName)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        DatabaseSchemaState targetState;

        try
        {
            await using var probe = new ProviderDbContext(request.TargetDatabasePath, false, false, logger);
            targetState = probe.IsUpgradeNeeded();
        }
        catch (DbException ex)
        {
            logger.Error($"Failed to merge into database '{request.TargetDatabasePath}': {ex.Message}");

            return DatabaseToolsOutcome.Failed;
        }

        if (targetState.CurrentVersion == DatabaseSchemaVersion.Unknown)
        {
            logger.Error($"{SchemaStateMessages.UnrecognizedSchema(SchemaStateMessages.TargetLabel, request.TargetDatabasePath)}");

            return DatabaseToolsOutcome.Failed;
        }

        if (targetState.NeedsUpgrade)
        {
            logger.Error($"Target database '{request.TargetDatabasePath}' is at schema v{targetState.CurrentVersion} but v{DatabaseSchemaVersion.Current} is required. Run the 'upgrade' command first.");

            return DatabaseToolsOutcome.Failed;
        }

        try
        {
            await using var targetContext = new ProviderDbContext(request.TargetDatabasePath, false, logger);

            // SQLite cannot translate composite IN, so query by name and narrow exact identities in memory.
            var identitiesAlreadyInTarget = new HashSet<ProviderIdentity>();

            for (var offset = 0; offset < sourceNames.Count; offset += ProviderSource.MaxInClauseParameters)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var chunk = sourceNames
                    .Skip(offset)
                    .Take(ProviderSource.MaxInClauseParameters)
                    .ToList();

                var targetKeys = await targetContext.ProviderDetails
                    .AsNoTracking()
                    .Where(p => chunk.Contains(p.ProviderName))
                    .Select(p => new { p.ProviderName, p.VersionKey })
                    .ToListAsync(cancellationToken);

                foreach (var key in targetKeys)
                {
                    var identity = new ProviderIdentity(key.ProviderName, key.VersionKey);

                    if (sourceIdentities.Contains(identity)) { identitiesAlreadyInTarget.Add(identity); }
                }
            }

            // Wrap deletes and inserts so cancellation or failure cannot leave providers permanently missing.
            await using var transaction = await targetContext.Database.BeginTransactionAsync(cancellationToken);

            if (identitiesAlreadyInTarget.Count > 0)
            {
                logger.Information($"The target database already contains {identitiesAlreadyInTarget.Count} of the source's provider version(s).");

                if (request.Overwrite)
                {
                    logger.Information($"Removing these provider version(s) from the target database...");

                    // Delete only colliding identities so unrelated versions of the same provider survive.
                    foreach (var identity in identitiesAlreadyInTarget)
                    {
                        targetContext.Entry(new ProviderDetails { ProviderName = identity.ProviderName, VersionKey = identity.VersionKey })
                            .State = EntityState.Deleted;
                    }

                    await targetContext.SaveChangesAsync(cancellationToken);
                    targetContext.ChangeTracker.Clear();

                    logger.Information($"Removal of {identitiesAlreadyInTarget.Count} provider version(s) completed.");
                }
                else
                {
                    logger.Information($"These provider version(s) will not be copied from the source.");
                }
            }

            logger.Information($"Copying provider versions from the source...");

            var skipForLoad = request.Overwrite ? null : identitiesAlreadyInTarget;

            var expectedCopiedIdentities = skipForLoad is null
                ? sourceIdentities
                : sourceIdentities.Where(identity => !skipForLoad.Contains(identity)).ToHashSet();

            var expectedCopiedNames = expectedCopiedIdentities
                .Select(identity => identity.ProviderName)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            logger.Information($"");
            LogProviderDetailHeader(logger, expectedCopiedNames);

            var copiedCount = 0;
            var pendingBatch = new List<ProviderDetails>(BatchSize);

            await foreach (var provider in ProviderSource.LoadProvidersAsync(
                request.SourcePath,
                logger,
                regex: null,
                skipIdentities: skipForLoad,
                preDiscoveredProviderNames: sourceNames,
                cancellationToken: cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                targetContext.ProviderDetails.Add(provider);
                pendingBatch.Add(provider);

                progress?.Report(new DatabaseToolsProgress(copiedCount + pendingBatch.Count, expectedCopiedIdentities.Count, provider.ProviderName));

                if (pendingBatch.Count < BatchSize) { continue; }

                copiedCount += await FlushBatchAsync(logger, targetContext, pendingBatch, cancellationToken);
            }

            if (pendingBatch.Count > 0)
            {
                copiedCount += await FlushBatchAsync(logger, targetContext, pendingBatch, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);

            logger.Information($"");
            logger.Information($"Copied {copiedCount} provider version(s).");

            return DatabaseToolsOutcome.Succeeded;
        }
        catch (OperationCanceledException)
        {
            return DatabaseToolsOutcome.Cancelled;
        }
    }

    private async Task<int> FlushBatchAsync(
        ITraceLogger logger,
        ProviderDbContext context,
        List<ProviderDetails> batch,
        CancellationToken cancellationToken)
    {
        await context.SaveChangesAsync(cancellationToken);

        foreach (var details in batch)
        {
            LogProviderDetails(logger, details);
        }

        var flushed = batch.Count;
        batch.Clear();
        context.ChangeTracker.Clear();

        return flushed;
    }
}
