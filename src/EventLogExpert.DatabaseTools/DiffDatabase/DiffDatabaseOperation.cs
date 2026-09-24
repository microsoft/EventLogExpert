// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common;
using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Database.Context;
using EventLogExpert.Provider.Resolution;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.DatabaseTools.DiffDatabase;

/// <summary>
///     Produces a new database containing providers from the second source that are NOT in the first. Defers
///     DbContext creation until at least one provider is about to be persisted so an empty result does not leave a stub
///     .db on disk.
/// </summary>
internal sealed class DiffDatabaseOperation(DiffDatabaseRequest request) : OperationBase, IDatabaseToolsOperation
{
    public async Task<DatabaseToolsOutcome> ExecuteAsync(
        IOperationLog logger,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!ProviderSource.TryValidate(request.FirstSourcePath, logger) ||
            !ProviderSource.TryValidate(request.SecondSourcePath, logger) ||
            !await ProviderSource.ValidateSourceSchemasAsync(request.FirstSourcePath, logger, cancellationToken) ||
            !await ProviderSource.ValidateSourceSchemasAsync(request.SecondSourcePath, logger, cancellationToken))
        {
            return DatabaseToolsOutcome.Failed;
        }

        if (File.Exists(request.NewDatabasePath))
        {
            logger.User(LogLevel.Error,
                new LocalizableText(DatabaseToolsLogKeys.DiffFileAlreadyExists, [request.NewDatabasePath]));

            return DatabaseToolsOutcome.Failed;
        }

        if (!string.Equals(Path.GetExtension(request.NewDatabasePath), ".db", StringComparison.OrdinalIgnoreCase))
        {
            logger.User(LogLevel.Error, new LocalizableText(DatabaseToolsLogKeys.DiffNewDbPathMustBeDb, []));

            return DatabaseToolsOutcome.Failed;
        }

        var firstIdentities = (await ProviderSource.LoadProviderIdentitiesAsync(
            request.FirstSourcePath,
            logger,
            cancellationToken: cancellationToken)).ToHashSet();

        var providersCopied = new List<ProviderDetails>();

        logger.User(LogLevel.Information,
            new LocalizableText(DatabaseToolsLogKeys.DiffSkippingSecondSourceDuplicates,
                [firstIdentities.Count.ToString()]));

        ProviderDbContext? newDbContext = null;

        try
        {
            await foreach (var details in ProviderSource.LoadProvidersAsync(request.SecondSourcePath,
                logger,
                regex: null,
                skipIdentities: firstIdentities,
                cancellationToken: cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                logger.User(LogLevel.Information,
                    new LocalizableText(DatabaseToolsLogKeys.DiffCopyingProvider, [details.ProviderName]));

                newDbContext ??= new ProviderDbContext(request.NewDatabasePath, false, logger.Trace);

                newDbContext.ProviderDetails.Add(details);

                providersCopied.Add(details);

                progress?.Report(new DatabaseToolsProgress(providersCopied.Count, null, details.ProviderName));
            }

            if (newDbContext is null)
            {
                logger.User(LogLevel.Warning, new LocalizableText(DatabaseToolsLogKeys.DiffNoMissingProviders, []));

                return DatabaseToolsOutcome.Succeeded;
            }

            await newDbContext.SaveChangesAsync(cancellationToken);

            logger.User(LogLevel.Information, new LocalizableText(DatabaseToolsLogKeys.DiffProvidersCopiedHeader, []));
            logger.Data(LogLevel.Information, string.Empty);
            LogProviderDetailHeader(logger, providersCopied.Select(p => p.ProviderName));

            foreach (var provider in providersCopied)
            {
                LogProviderDetails(logger, provider);
            }

            return DatabaseToolsOutcome.Succeeded;
        }
        catch (OperationCanceledException)
        {
            await CleanupPartialDatabaseAsync(logger, newDbContext, request.NewDatabasePath);
            newDbContext = null;

            return DatabaseToolsOutcome.Cancelled;
        }
        catch (Exception ex)
        {
            // Any non-cancellation failure (e.g., EF/SQLite errors mid-save) - no stub .db.
            logger.User(LogLevel.Error, new LocalizableText(DatabaseToolsLogKeys.DiffUnexpectedError, []), ex);
            await CleanupPartialDatabaseAsync(logger, newDbContext, request.NewDatabasePath);
            newDbContext = null;

            return DatabaseToolsOutcome.Failed;
        }
        finally
        {
            if (newDbContext is not null) { await newDbContext.DisposeAsync(); }
        }
    }
}
