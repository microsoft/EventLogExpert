// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Contracts;
using EventLogExpert.DatabaseTools.Sources;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Models;
using EventLogExpert.ProviderDatabase.Context;
using Microsoft.Data.Sqlite;
using System.Text.RegularExpressions;

namespace EventLogExpert.DatabaseTools.Operations;

/// <summary>
///     Creates a new provider database (.db). When the request's SourcePath is null/empty, local providers on this
///     machine are used. When supplied, ONLY the source is used (no fallback to local providers). Streams provider details
///     into the DbContext in batches; defers DbContext creation until at least one provider is resolved so a failed scan
///     does not leave an empty .db on disk.
/// </summary>
public sealed class CreateDatabaseOperation(CreateDatabaseRequest request) : OperationBase, IDatabaseToolsOperation
{
    private const int BatchSize = 100;

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

        if (request.SourcePath is not null && !ProviderSource.TryValidate(request.SourcePath, logger))
        {
            return DatabaseToolsOutcome.Failed;
        }

        HashSet<string> skipProviderNames = new(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(request.SkipProvidersInFile))
        {
            if (!ProviderSource.TryValidate(request.SkipProvidersInFile, logger))
            {
                return DatabaseToolsOutcome.Failed;
            }

            foreach (var name in ProviderSource.LoadProviderNames(request.SkipProvidersInFile, logger))
            {
                skipProviderNames.Add(name);
            }

            logger.Information($"Found {skipProviderNames.Count} providers in {request.SkipProvidersInFile}. These will not be included in the new database.");
        }

        var count = 0;
        var headerLogged = false;
        var pendingForHeader = new List<ProviderDetails>(BatchSize);

        // Defer creating the DbContext (and therefore the .db file on disk) until we have
        // at least one provider to persist. This prevents leaving an empty database behind
        // when no provider details could be resolved.
        ProviderDbContext? dbContext = null;

        try
        {
            IEnumerable<ProviderDetails> providersToAdd = request.SourcePath is null
                ? LoadLocalProviders(logger, request.FilterRegex, skipProviderNames)
                : ProviderSource.LoadProviders(request.SourcePath, logger, request.FilterRegex, skipProviderNames);

            foreach (var details in providersToAdd)
            {
                cancellationToken.ThrowIfCancellationRequested();

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
                count += pendingForHeader.Count;
                await FlushHeaderAndBufferAsync(logger, dbContext, pendingForHeader, cancellationToken);
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
            // EF Core's SqliteConnection pool keeps the file handle alive across DbContext.Dispose, so a naive
            // File.Delete hits a sharing violation on Windows. Mirror the codebase's documented cleanup pattern
            // (ProviderDatabaseMaintenance.PrepareForFileDeletion / SqliteTestDb.Delete): dispose the context,
            // clear the pool, THEN delete the partial .db so the user's path is left clean.
            dbContext?.Dispose();
            dbContext = null;

            if (File.Exists(request.TargetPath))
            {
                SqliteConnection.ClearAllPools();

                try { File.Delete(request.TargetPath); }
                catch (IOException ex)
                {
                    logger.Warning($"Could not delete partial database at {request.TargetPath}: {ex.Message}. Delete manually before next create.");
                }
                catch (UnauthorizedAccessException ex)
                {
                    logger.Warning($"Could not delete partial database at {request.TargetPath}: {ex.Message}. Delete manually before next create.");
                }
            }

            return DatabaseToolsOutcome.Cancelled;
        }
        catch (RegexMatchTimeoutException)
        {
            logger.Error($"The provider-name regex timed out. The pattern may cause catastrophic backtracking.");

            return DatabaseToolsOutcome.Failed;
        }
        finally
        {
            dbContext?.Dispose();
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
}
