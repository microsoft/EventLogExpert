// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Resolution;
using EventLogExpert.ProviderDatabase.Context;

namespace EventLogExpert.DatabaseTools.DiffDatabase;

/// <summary>
///     Produces a new database containing providers from the second source that are NOT in the first. Defers
///     DbContext creation until at least one provider is about to be persisted so an empty result does not leave a stub
///     .db on disk.
/// </summary>
internal sealed class DiffDatabaseOperation(DiffDatabaseRequest request) : OperationBase, IDatabaseToolsOperation
{
    public async Task<DatabaseToolsOutcome> ExecuteAsync(
        ITraceLogger logger,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!ProviderSource.TryValidate(request.FirstSourcePath, logger) ||
            !ProviderSource.TryValidate(request.SecondSourcePath, logger) ||
            !ProviderSource.ValidateSourceSchemas(request.FirstSourcePath, logger) ||
            !ProviderSource.ValidateSourceSchemas(request.SecondSourcePath, logger))
        {
            return DatabaseToolsOutcome.Failed;
        }

        if (File.Exists(request.NewDatabasePath))
        {
            logger.Error($"File already exists: {request.NewDatabasePath}");

            return DatabaseToolsOutcome.Failed;
        }

        if (!string.Equals(Path.GetExtension(request.NewDatabasePath), ".db", StringComparison.OrdinalIgnoreCase))
        {
            logger.Error($"New db path must have a .db extension.");

            return DatabaseToolsOutcome.Failed;
        }

        var firstProviderNames = new HashSet<string>(
            ProviderSource.LoadProviderNames(request.FirstSourcePath, logger),
            StringComparer.OrdinalIgnoreCase);

        var providersCopied = new List<ProviderDetails>();

        logger.Information(
            $"Skipping up to {firstProviderNames.Count} provider name(s) from the second source that also appear in the first source.");

        ProviderDbContext? newDbContext = null;

        try
        {
            foreach (var details in ProviderSource.LoadProviders(request.SecondSourcePath,
                logger,
                regex: null,
                skipProviderNames: firstProviderNames))
            {
                cancellationToken.ThrowIfCancellationRequested();

                logger.Information(
                    $"Copying {details.ProviderName} because it is present in second source but not first.");

                newDbContext ??= new ProviderDbContext(request.NewDatabasePath, false, logger);

                newDbContext.ProviderDetails.Add(details);

                providersCopied.Add(details);

                progress?.Report(new DatabaseToolsProgress(providersCopied.Count, null, details.ProviderName));
            }

            if (newDbContext is null)
            {
                logger.Warning(
                    $"No providers in the second source are missing from the first. Database was not created.");

                return DatabaseToolsOutcome.Succeeded;
            }

            await newDbContext.SaveChangesAsync(cancellationToken);

            logger.Information($"Providers copied to new database:");
            logger.Information($"");
            LogProviderDetailHeader(logger, providersCopied.Select(p => p.ProviderName));

            foreach (var provider in providersCopied)
            {
                LogProviderDetails(logger, provider);
            }

            return DatabaseToolsOutcome.Succeeded;
        }
        catch (OperationCanceledException)
        {
            CleanupPartialDatabase(logger, ref newDbContext, request.NewDatabasePath);
            return DatabaseToolsOutcome.Cancelled;
        }
        catch (Exception ex)
        {
            // Any non-cancellation failure (e.g., EF/SQLite errors mid-save) — no stub .db.
            logger.Error($"Unexpected error diffing databases: {ex.Message}");
            CleanupPartialDatabase(logger, ref newDbContext, request.NewDatabasePath);
            return DatabaseToolsOutcome.Failed;
        }
        finally
        {
            newDbContext?.Dispose();
        }
    }
}
