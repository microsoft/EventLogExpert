// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventProviderDatabase;
using System.CommandLine;

namespace EventLogExpert.EventDbTool;

public class UpgradeDatabaseCommand : DbToolCommand
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
            UpgradeDatabase(action.GetRequiredValue(fileArgument), action.GetValue(verboseOption)));

        return upgradeDatabaseCommand;
    }

    private static void UpgradeDatabase(string file, bool verbose)
    {
        if (!File.Exists(file))
        {
            Console.WriteLine($"File not found: {file}");
            return;
        }

        using var dbContext = new EventProviderDbContext(file, false);

        var (needsV2, needsV3) = dbContext.IsUpgradeNeeded();

        if (!(needsV2 || needsV3))
        {
            Console.WriteLine("This database does not need to be upgraded.");
            return;
        }

        dbContext.PerformUpgradeIfNeeded();
    }
}
