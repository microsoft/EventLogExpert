// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventProviderDatabase;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Providers;
using Microsoft.Extensions.Logging;
using System.CommandLine;

namespace EventLogExpert.EventDbTool;

public sealed class CreateDatabaseCommand : DbToolCommand
{
    private static readonly ITraceLogger s_logger = new TraceLogger(LogLevel.Information);

    public static Command GetCommand()
    {
        var createDatabaseCommand = new Command(
            name: "create",
            description: "Creates a new event database.");
        var fileArgument = new Argument<string>(
            name: "file",
            description: "File to create. Must have a .db extension.");
        var filterOption = new Option<string>(
            name: "--filter",
            description: "Only providers matching specified regex string will be added to the database.");
        var skipProvidersInFileOption = new Option<string>(
            name: "--skip-providers-in-file",
            description: "Any providers found in the specified database file will not be included in the new database. " +
                "For example, when creating a database of event providers for Exchange Server, it may be useful " +
                "to provide a database of all providers from a fresh OS install with no other products. That way, all the " +
                "OS providers are skipped, and only providers added by Exchange or other installed products " +
                "would be saved in the new database.");
        var verboseOption = new Option<bool>(
            name: "--verbose",
            description: "Enable verbose logging. May be useful for troubleshooting.");

        createDatabaseCommand.AddArgument(fileArgument);
        createDatabaseCommand.AddOption(filterOption);
        createDatabaseCommand.AddOption(skipProvidersInFileOption);
        createDatabaseCommand.AddOption(verboseOption);
        createDatabaseCommand.SetHandler(CreateDatabase, fileArgument, filterOption, verboseOption, skipProvidersInFileOption);

        return createDatabaseCommand;
    }

    public static void CreateDatabase(string path, string filter, bool verboseLogging, string skipProvidersInFile)
    {
        if (File.Exists(path))
        {
            Console.WriteLine($"Cannot create database because file already exists: {path}");
            return;
        }

        if (Path.GetExtension(path) != ".db")
        {
            Console.WriteLine("File extension must be .db.");
            return;
        }

        HashSet<string> skipProviderNames = [];

        if (!string.IsNullOrWhiteSpace(skipProvidersInFile))
        {
            if (!File.Exists(skipProvidersInFile))
            {
                Console.WriteLine($"File not found: {skipProvidersInFile}");
            }

            using var skipDbContext = new EventProviderDbContext(skipProvidersInFile, readOnly: true);
            foreach (var provider in skipDbContext.ProviderDetails)
            {
                skipProviderNames.Add(provider.ProviderName);
            }

            Console.WriteLine($"Found {skipProviderNames.Count} providers in file {skipProvidersInFile}. " +
                "These will not be included in the new database.");
        }

        var providerNames = GetLocalProviderNames(filter);

        if (!providerNames.Any())
        {
            Console.WriteLine($"No providers found matching filter {filter}.");
            return;
        }

        var providerNamesNotSkipped = providerNames.Where(name => !skipProviderNames.Contains(name)).ToList();

        var numberSkipped = providerNames.Count - providerNamesNotSkipped.Count;
        if (numberSkipped > 0)
        {
            Console.WriteLine($"{numberSkipped} providers were skipped due to being present in the specified database.");
        }

        using var dbContext = new EventProviderDbContext(path, readOnly: false);

        LogProviderDetailHeader(providerNamesNotSkipped);

        foreach (var providerName in providerNamesNotSkipped)
        {
            var provider = new EventMessageProvider(providerName, verboseLogging ? s_logger : null);

            var details = provider.LoadProviderDetails();

            dbContext.ProviderDetails.Add(details);

            LogProviderDetails(details);
        }

        Console.WriteLine();
        Console.WriteLine("Saving database. Please wait...");

        dbContext.SaveChanges();

        Console.WriteLine("Done!");
    }
}
