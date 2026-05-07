// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventProviderDatabase;
using EventLogExpert.Eventing.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.CommandLine;
using System.Data.Common;

namespace EventLogExpert.EventDbTool;

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
        ProviderDatabaseSchemaState state;

        try
        {
            using var probe = new EventProviderDbContext(file, readOnly: false, ensureCreated: false, Logger);
            state = probe.IsUpgradeNeeded();
        }
        catch (DbException ex)
        {
            Logger.Error($"Failed to upgrade database '{file}': {ex.Message}");

            return;
        }

        if (!state.NeedsUpgrade)
        {
            Logger.Info($"This database does not need to be upgraded.");

            return;
        }

        if (state.CurrentVersion == ProviderDatabaseSchemaVersion.Unknown)
        {
            Logger.Error($"{DatabaseSchemaMessages.UnrecognizedSchema(DatabaseSchemaMessages.DefaultLabel, file)}");

            return;
        }

        if (state.CurrentVersion is 1 or 2)
        {
            Logger.Error($"{DatabaseSchemaMessages.UnsupportedV1OrV2Schema(file, state.CurrentVersion)}");

            return;
        }

        // Destructive ops beyond this point — exceptions propagate so partial-state failures
        // aren't masked as a benign "upgrade failed". DatabaseUpgradeException only fires from
        // ProviderDetailsMerger before any DROP TABLE, so it's safe to catch.
        using var upgradeContext = new EventProviderDbContext(file, false, Logger);

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
