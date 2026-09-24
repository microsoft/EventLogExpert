// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common;
using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Database.Context;
using EventLogExpert.Provider.Schema;
using Microsoft.Extensions.Logging;
using System.Data.Common;

namespace EventLogExpert.DatabaseTools.UpgradeDatabase;

/// <summary>
///     Upgrades the schema of an existing provider database to the current version. Probes the schema in its own
///     scope first so a corrupt or non-SQLite file produces a friendly error before any destructive operation begins.
/// </summary>
internal sealed class UpgradeDatabaseOperation(UpgradeDatabaseRequest request) : OperationBase, IDatabaseToolsOperation
{
    public async Task<DatabaseToolsOutcome> ExecuteAsync(
        IOperationLog logger,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(request.DatabasePath))
        {
            logger.User(LogLevel.Error,
                new LocalizableText(DatabaseToolsLogKeys.UpgradeFileNotFound, [request.DatabasePath]));

            return DatabaseToolsOutcome.Failed;
        }

        DatabaseSchemaState state;

        try
        {
            await using var probe = new ProviderDbContext(request.DatabasePath, false, false, logger.Trace);
            state = probe.IsUpgradeNeeded();
        }
        catch (DbException ex)
        {
            logger.User(LogLevel.Error,
                new LocalizableText(DatabaseToolsLogKeys.UpgradeOpenDatabaseFailed, [request.DatabasePath]),
                ex);

            return DatabaseToolsOutcome.Failed;
        }
        catch (SchemaLockTimeoutException ex)
        {
            logger.User(LogLevel.Error,
                new LocalizableText(DatabaseToolsLogKeys.UpgradeCannotOpenDatabase, [request.DatabasePath]),
                ex);

            return DatabaseToolsOutcome.Failed;
        }

        if (!state.NeedsUpgrade)
        {
            logger.User(LogLevel.Information, new LocalizableText(DatabaseToolsLogKeys.UpgradeNotNeeded, []));

            return DatabaseToolsOutcome.Succeeded;
        }

        if (state.CurrentVersion == DatabaseSchemaVersion.Unknown)
        {
            logger.User(LogLevel.Error,
                new LocalizableText(DatabaseToolsLogKeys.SchemaUnrecognizedDefault, [request.DatabasePath]));

            return DatabaseToolsOutcome.Failed;
        }

        if (state.CurrentVersion is 1 or 2)
        {
            logger.User(LogLevel.Error,
                new LocalizableText(DatabaseToolsLogKeys.UpgradeUnsupportedV1OrV2Schema,
                    [request.DatabasePath, state.CurrentVersion.ToString()]));

            return DatabaseToolsOutcome.Failed;
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await using var upgradeContext = new ProviderDbContext(request.DatabasePath, false, false, logger.Trace);

            upgradeContext.PerformUpgradeIfNeeded();

            return DatabaseToolsOutcome.Succeeded;
        }
        catch (OperationCanceledException)
        {
            return DatabaseToolsOutcome.Cancelled;
        }
        catch (DatabaseUpgradeException ex)
        {
            logger.User(LogLevel.Error, new LocalizableText(DatabaseToolsLogKeys.UpgradeDatabaseUpgradeFailed, []), ex);

            return DatabaseToolsOutcome.Failed;
        }
        catch (SchemaLockTimeoutException ex)
        {
            logger.User(LogLevel.Error,
                new LocalizableText(DatabaseToolsLogKeys.UpgradeCannotOpenDatabase, [request.DatabasePath]),
                ex);

            return DatabaseToolsOutcome.Failed;
        }
    }
}
