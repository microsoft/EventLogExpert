// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.EventProviderDatabase;
using EventLogExpert.Library.Models;
using EventLogExpert.Library.Providers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.CommandLine;
using System.Text.Json;

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

        if (!dbContext.IsUpgradeNeeded())
        {
            Console.WriteLine("This database does not need to be upgraded.");
            return;
        }

        dbContext.PerformUpgradeIfNeeded();
    }
}
