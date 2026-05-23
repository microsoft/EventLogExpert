// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Schema;
using EventLogExpert.ProviderDatabase.Context;
using Microsoft.Extensions.DependencyInjection;
using System.CommandLine;
using System.Data.Common;

namespace EventLogExpert.EventDbTool.Commands;

public class UpgradeDatabaseCommand(ITraceLogger logger) : DbToolCommand(logger)
{
    public static Command GetCommand()
    {
        Command upgradeDatabaseCommand = new(
            "upgrade",
            "Upgrades the database schema");

        Argument<string> fileArgument = new("file")
        {
            Description = "The database file to upgrade."
        };

        Option<bool> verboseOption = new("--verbose")
        {
            Description = "Verbose logging. May be useful for troubleshooting."
        };

        upgradeDatabaseCommand.Arguments.Add(fileArgument);
        upgradeDatabaseCommand.Options.Add(verboseOption);

        upgradeDatabaseCommand.SetAction(action =>
        {
            using var sp = Program.BuildServiceProvider(action.GetValue(verboseOption));
            new UpgradeDatabaseCommand(sp.GetRequiredService<ITraceLogger>())
                .UpgradeDatabase(action.GetRequiredValue(fileArgument));
        });

        return upgradeDatabaseCommand;
    }

    internal void UpgradeDatabase(string file)
    {
        if (!File.Exists(file))
        {
            Logger.Error($"File not found: {file}");
            return;
        }

        // Probe the schema in its own scope so a corrupt or non-SQLite file produces a friendly
        // error instead of a stack trace.
        DatabaseSchemaState state;

        try
        {
            using var probe = new ProviderDbContext(file, false, false, Logger);
            state = probe.IsUpgradeNeeded();
        }
        catch (DbException ex)
        {
            Logger.Error($"Failed to upgrade database '{file}': {ex.Message}");

            return;
        }

        if (!state.NeedsUpgrade)
        {
            Logger.Information($"This database does not need to be upgraded.");

            return;
        }

        if (state.CurrentVersion == DatabaseSchemaVersion.Unknown)
        {
            Logger.Error($"{SchemaStateMessages.UnrecognizedSchema(SchemaStateMessages.DefaultLabel, file)}");

            return;
        }

        if (state.CurrentVersion is 1 or 2)
        {
            Logger.Error($"{SchemaStateMessages.UnsupportedV1OrV2Schema(file, state.CurrentVersion)}");

            return;
        }

        // Destructive ops beyond this point — exceptions propagate so partial-state failures
        // aren't masked as a benign "upgrade failed". DatabaseUpgradeException only fires from
        // ProviderDetailsMerger before any DROP TABLE, so it's safe to catch.
        using var upgradeContext = new ProviderDbContext(file, false, Logger);

        try
        {
            upgradeContext.PerformUpgradeIfNeeded();
        }
        catch (DatabaseUpgradeException ex)
        {
            Logger.Error($"{ex.Message}");
        }
    }
}
