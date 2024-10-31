// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventProviderDatabase;
using System.CommandLine;

namespace EventLogExpert.EventDbTool;

public class UpgradeDatabaseCommand : DbToolCommand
{
    public static Command GetCommand()
    {
        var upgradeDatabaseCommand = new Command(
            name: "upgrade",
            description: "Upgrades the database schema");
        var fileArgument = new Argument<string>(
            name: "file",
            description: "The database file to upgrade.");
        var verboseOption = new Option<bool>(
            name: "--verbose",
            description: "Verbose logging. May be useful for troubleshooting.");
        upgradeDatabaseCommand.AddArgument(fileArgument);
        upgradeDatabaseCommand.AddOption(verboseOption);
        upgradeDatabaseCommand.SetHandler((fileArgumentValue, verboseOptionValue) =>
        {
            UpgradeDatabase(fileArgumentValue, verboseOptionValue);
        },
        fileArgument, verboseOption);

        return upgradeDatabaseCommand;
    }

    public static void UpgradeDatabase(string file, bool verbose)
    {
        if (!File.Exists(file))
        {
            Console.WriteLine($"File not found: {file}");
            return;
        }

        using var dbContext = new EventProviderDbContext(file, readOnly: false);

        var (needsV2, needsV3) = dbContext.IsUpgradeNeeded();

        if (!(needsV2 || needsV3))
        {
            Console.WriteLine("This database does not need to be upgraded.");
            return;
        }

        dbContext.PerformUpgradeIfNeeded();
    }
}
