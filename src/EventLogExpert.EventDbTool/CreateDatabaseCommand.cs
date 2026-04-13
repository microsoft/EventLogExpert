// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventProviderDatabase;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Providers;
using Microsoft.Extensions.DependencyInjection;
using System.CommandLine;

namespace EventLogExpert.EventDbTool;

public sealed class CreateDatabaseCommand(ITraceLogger logger) : DbToolCommand(logger)
{
    public static Command GetCommand()
    {
        Command createDatabaseCommand = new("create", "Creates a new event database.");

        Argument<string> fileArgument = new("file")
        {
            Description = "File to create. Must have a .db extension."
        };

        Option<string> filterOption = new("--filter")
        {
            Description = "Only providers matching specified regex string will be added to the database."
        };

        Option<string> skipProvidersInFileOption = new("--skip-providers-in-file")
        {
            Description =
                "Any providers found in the specified database file will not be included in the new database. " +
                "For example, when creating a database of event providers for Exchange Server, it may be useful " +
                "to provide a database of all providers from a fresh OS install with no other products. That way, all the " +
                "OS providers are skipped, and only providers added by Exchange or other installed products " +
                "would be saved in the new database."
        };

        Option<bool> verboseOption = new("--verbose")
        {
            Description = "Enable verbose logging. May be useful for troubleshooting."
        };

        createDatabaseCommand.Arguments.Add(fileArgument);
        createDatabaseCommand.Options.Add(filterOption);
        createDatabaseCommand.Options.Add(skipProvidersInFileOption);
        createDatabaseCommand.Options.Add(verboseOption);

        createDatabaseCommand.SetAction(result =>
        {
            using var sp = Program.BuildServiceProvider(result.GetValue(verboseOption));
            new CreateDatabaseCommand(sp.GetRequiredService<ITraceLogger>())
                .CreateDatabase(
                    result.GetRequiredValue(fileArgument),
                    result.GetValue(filterOption),
                    result.GetValue(skipProvidersInFileOption));
        });

        return createDatabaseCommand;
    }

    private void CreateDatabase(string path, string? filter, string? skipProvidersInFile)
    {
        if (File.Exists(path))
        {
            Logger.Error($"Cannot create database because file already exists: {path}");
            return;
        }

        if (Path.GetExtension(path) != ".db")
        {
            Logger.Error($"File extension must be .db.");
            return;
        }

        HashSet<string> skipProviderNames = [];

        if (!string.IsNullOrWhiteSpace(skipProvidersInFile))
        {
            if (!File.Exists(skipProvidersInFile))
            {
                Logger.Error($"File not found: {skipProvidersInFile}");
                return;
            }

            using var skipDbContext = new EventProviderDbContext(skipProvidersInFile, true, Logger);

            foreach (var provider in skipDbContext.ProviderDetails)
            {
                skipProviderNames.Add(provider.ProviderName);
            }

            Logger.Info($"Found {skipProviderNames.Count} providers in file {skipProvidersInFile}. These will not be included in the new database.");
        }

        var providerNames = GetLocalProviderNames(filter);

        if (!providerNames.Any())
        {
            Logger.Warn($"No providers found matching filter {filter}.");
            return;
        }

        var providerNamesNotSkipped = providerNames.Where(name => !skipProviderNames.Contains(name)).ToList();

        var numberSkipped = providerNames.Count - providerNamesNotSkipped.Count;

        if (numberSkipped > 0)
        {
            Logger.Info($"{numberSkipped} providers were skipped due to being present in the specified database.");
        }

        using var dbContext = new EventProviderDbContext(path, false, Logger);

        LogProviderDetailHeader(providerNamesNotSkipped);

        foreach (var providerName in providerNamesNotSkipped)
        {
            var provider = new EventMessageProvider(providerName, Logger);

            var details = provider.LoadProviderDetails();

            dbContext.ProviderDetails.Add(details);

            LogProviderDetails(details);
        }

        Logger.Info($"");
        Logger.Info($"Saving database. Please wait...");

        dbContext.SaveChanges();

        Logger.Info($"Done!");
    }
}
