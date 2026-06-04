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

/// <summary>
///     Merges providers from a source into a target database. Providers already in the target are skipped by default;
///     when <see cref="MergeDatabaseRequest.Overwrite" /> is true, existing target rows for those providers are removed
///     before re-inserting from the source.
/// </summary>
internal sealed class MergeDatabaseOperation(MergeDatabaseRequest request) : OperationBase, IDatabaseToolsOperation
{
    private const int BatchSize = 100;

    public async Task<DatabaseToolsOutcome> ExecuteAsync(
        ITraceLogger logger,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!ProviderSource.TryValidate(request.SourcePath, logger))
        {
            return DatabaseToolsOutcome.Failed;
        }

        if (!File.Exists(request.TargetDatabasePath))
        {
            logger.Error($"File not found: {request.TargetDatabasePath}");

            return DatabaseToolsOutcome.Failed;
        }

        var sourceNames = new HashSet<string>(
            ProviderSource.LoadProviderNames(request.SourcePath, logger),
            StringComparer.OrdinalIgnoreCase);

        if (sourceNames.Count == 0)
        {
            logger.Warning($"No providers were discovered in the source.");

            return DatabaseToolsOutcome.Succeeded;
        }

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

            var sourceNamesList = sourceNames.ToList();
            var targetMatchingNames = new List<string>();

            for (var offset = 0; offset < sourceNamesList.Count; offset += ProviderSource.MaxInClauseParameters)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var chunk = sourceNamesList
                    .Skip(offset)
                    .Take(ProviderSource.MaxInClauseParameters)
                    .ToList();

                targetMatchingNames.AddRange(
                    targetContext.ProviderDetails
                        .AsNoTracking()
                        .Where(p => chunk.Contains(p.ProviderName))
                        .Select(p => p.ProviderName));
            }

            var providerNamesInTarget = new HashSet<string>(targetMatchingNames, StringComparer.OrdinalIgnoreCase);

            // Wrap the destructive phase (overwrite delete + provider copy) in a transaction so a cancel mid-flight
            // does not leave the target with permanently-missing providers. The non-overwrite path is also wrapped
            // so partial copies don't persist on failure — re-running merge produces the intended end state regardless.
            await using var transaction = await targetContext.Database.BeginTransactionAsync(cancellationToken);

            if (targetMatchingNames.Count > 0)
            {
                logger.Information($"The target database contains {targetMatchingNames.Count} provider row(s) matching {providerNamesInTarget.Count} provider name(s) in the source.");

                if (request.Overwrite)
                {
                    logger.Information($"Removing these providers from the target database...");

                    var removed = 0;

                    for (var offset = 0; offset < targetMatchingNames.Count; offset += ProviderSource.MaxInClauseParameters)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var chunk = targetMatchingNames
                            .Skip(offset)
                            .Take(ProviderSource.MaxInClauseParameters)
                            .ToList();

                        removed += await targetContext.ProviderDetails
                            .Where(p => chunk.Contains(p.ProviderName))
                            .ExecuteDeleteAsync(cancellationToken);
                    }

                    logger.Information($"Removal of {removed} provider row(s) completed.");
                }
                else
                {
                    logger.Information($"These providers will not be copied from the source.");
                }
            }

            logger.Information($"Copying providers from the source...");

            var skipForLoad = request.Overwrite ? null : providerNamesInTarget;

            var expectedCopiedNames = skipForLoad is null
                ? sourceNames.ToList()
                : sourceNames.Where(n => !skipForLoad.Contains(n)).ToList();

            logger.Information($"");
            LogProviderDetailHeader(logger, expectedCopiedNames);

            var copiedCount = 0;
            var pendingBatch = new List<ProviderDetails>(BatchSize);

            foreach (var provider in ProviderSource.LoadProviders(
                request.SourcePath,
                logger,
                regex: null,
                skipProviderNames: skipForLoad,
                preDiscoveredProviderNames: sourceNamesList))
            {
                cancellationToken.ThrowIfCancellationRequested();

                targetContext.ProviderDetails.Add(provider);
                pendingBatch.Add(provider);

                progress?.Report(new DatabaseToolsProgress(copiedCount + pendingBatch.Count, expectedCopiedNames.Count, provider.ProviderName));

                if (pendingBatch.Count < BatchSize) { continue; }

                copiedCount += await FlushBatchAsync(logger, targetContext, pendingBatch, cancellationToken);
            }

            if (pendingBatch.Count > 0)
            {
                copiedCount += await FlushBatchAsync(logger, targetContext, pendingBatch, cancellationToken);
            }

            // Commit only after every delete + insert succeeded. Disposing the transaction without committing
            // (e.g., on OperationCanceledException or a thrown exception) rolls back the destructive phase.
            await transaction.CommitAsync(cancellationToken);

            logger.Information($"");
            logger.Information($"Copied {copiedCount} provider(s).");

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
