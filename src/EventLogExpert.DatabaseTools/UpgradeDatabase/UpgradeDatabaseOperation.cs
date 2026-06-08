// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Schema;
using EventLogExpert.ProviderDatabase.Context;
using System.Data.Common;

namespace EventLogExpert.DatabaseTools.UpgradeDatabase;

/// <summary>
///     Upgrades the schema of an existing provider database to the current version. Probes the schema in its own
///     scope first so a corrupt or non-SQLite file produces a friendly error before any destructive operation begins.
/// </summary>
internal sealed class UpgradeDatabaseOperation(UpgradeDatabaseRequest request) : OperationBase, IDatabaseToolsOperation
{
    public async Task<DatabaseToolsOutcome> ExecuteAsync(
        ITraceLogger logger,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(request.DatabasePath))
        {
            logger.Error($"File not found: {request.DatabasePath}");

            return DatabaseToolsOutcome.Failed;
        }

        DatabaseSchemaState state;

        try
        {
            await using var probe = new ProviderDbContext(request.DatabasePath, false, false, logger);
            state = probe.IsUpgradeNeeded();
        }
        catch (DbException ex)
        {
            logger.Error($"Failed to upgrade database '{request.DatabasePath}': {ex.Message}");

            return DatabaseToolsOutcome.Failed;
        }

        if (!state.NeedsUpgrade)
        {
            logger.Information($"This database does not need to be upgraded.");

            return DatabaseToolsOutcome.Succeeded;
        }

        if (state.CurrentVersion == DatabaseSchemaVersion.Unknown)
        {
            logger.Error($"{SchemaStateMessages.UnrecognizedSchema(SchemaStateMessages.DefaultLabel, request.DatabasePath)}");

            return DatabaseToolsOutcome.Failed;
        }

        if (state.CurrentVersion is 1 or 2)
        {
            logger.Error($"{SchemaStateMessages.UnsupportedV1OrV2Schema(request.DatabasePath, state.CurrentVersion)}");

            return DatabaseToolsOutcome.Failed;
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await using var upgradeContext = new ProviderDbContext(request.DatabasePath, false, logger);

            upgradeContext.PerformUpgradeIfNeeded();

            return DatabaseToolsOutcome.Succeeded;
        }
        catch (OperationCanceledException)
        {
            return DatabaseToolsOutcome.Cancelled;
        }
        catch (DatabaseUpgradeException ex)
        {
            logger.Error($"{ex.Message}");

            return DatabaseToolsOutcome.Failed;
        }
    }
}
