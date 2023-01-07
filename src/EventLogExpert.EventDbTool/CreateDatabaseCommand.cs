// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.EventProviderDatabase;
using EventLogExpert.Library.Providers;
using System.CommandLine;

namespace EventLogExpert.EventDbTool;

public class CreateDatabaseCommand : DbToolCommand
{
    public static Command GetCommand()
    {
        var createDatabaseCommand = new Command(
            name: "create",
            description: "Creates a new event database.");
        var fileOption = new Option<string>(
            name: "--file",
            description: "File to create. Must have a .db extension.");
        var filterOption = new Option<string>(
            name: "--filter",
            description: "Only providers matching specified regex string will be added to the database.");
        var verboseOption = new Option<bool>(
            name: "--verbose",
            description: "Enable verbose logging. May be useful for troubleshooting.");
        createDatabaseCommand.AddOption(fileOption);
        createDatabaseCommand.AddOption(filterOption);
        createDatabaseCommand.AddOption(verboseOption);
        createDatabaseCommand.SetHandler((fileOptionValue, filterOptionValue, verboseOptionValue) =>
        {
            CreateDatabase(fileOptionValue, filterOptionValue, verboseOptionValue);
        },
        fileOption, filterOption, verboseOption);

        return createDatabaseCommand;
    }

    public static void CreateDatabase(string path, string filter, bool verboseLogging)
    {
        if (File.Exists(path))
        {
            Console.WriteLine($"Cannot create database because file already exists: {path}");
            return;
        }

        var providerNames = GetProviderNames(filter);
        if (!providerNames.Any())
        {
            Console.WriteLine($"No providers found matching filter {filter}.");
            return;
        }

        var dbContext = new EventProviderDbContext(path, readOnly: false);

        LogProviderDetailHeader(providerNames);

        foreach (var providerName in providerNames.Distinct())
        {
            var provider = new EventMessageProvider(providerName, verboseLogging ? s => Console.WriteLine(s) : s => { });
            var details = provider.LoadProviderDetails();
            if (details != null)
            {
                dbContext.ProviderDetails.Add(details);

                LogProviderDetails(details);

                details = null;
            }
        }

        Console.WriteLine();
        Console.WriteLine("Saving database. Please wait...");

        dbContext.SaveChanges();
        dbContext.Dispose();

        Console.WriteLine("Done!");
    }
}
