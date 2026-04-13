// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventProviderDatabase;
using EventLogExpert.Eventing.Helpers;
using Microsoft.Extensions.DependencyInjection;
using System.CommandLine;

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

    private void UpgradeDatabase(string file)
    {
        if (!File.Exists(file))
        {
            Logger.Error($"File not found: {file}");
            return;
        }

        using var dbContext = new EventProviderDbContext(file, false, Logger);

        var (needsV2, needsV3) = dbContext.IsUpgradeNeeded();

        if (!(needsV2 || needsV3))
        {
            Logger.Info($"This database does not need to be upgraded.");
            return;
        }

        dbContext.PerformUpgradeIfNeeded();
    }
}
