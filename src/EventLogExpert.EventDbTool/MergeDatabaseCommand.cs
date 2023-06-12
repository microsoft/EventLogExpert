// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Providers;
using EventLogExpert.Eventing.EventProviderDatabase;
using System.CommandLine;

namespace EventLogExpert.EventDbTool;

public class MergeDatabaseCommand : DbToolCommand
{
    public static Command GetCommand()
    {
        var mergeDatabaseCommand = new Command(
            name: "merge",
            description: "Copies providers from a source into a target database.");
        var sourceDatabaseArgument = new Argument<string>(
            name: "source db",
            description: "The source database from which to copy providers.");
        var targetDatabaseArgument = new Argument<string>(
            name: "target db",
            description: "The target database.");
        var overwriteOption = new Option<bool>(
            name: "--overwrite",
            description: "When a provider from the source already exists in the target, overwrite the target" +
                " data with the source data. The default is to skip providers that already exist.");
        var verboseOption = new Option<bool>(
            name: "--verbose",
            description: "Enable verbose logging. May be useful for troubleshooting.");
        mergeDatabaseCommand.AddArgument(sourceDatabaseArgument);
        mergeDatabaseCommand.AddArgument(targetDatabaseArgument);
        mergeDatabaseCommand.AddOption(overwriteOption);
        mergeDatabaseCommand.AddOption(verboseOption);
        mergeDatabaseCommand.SetHandler((sourceArgumentValue, targetArgumentValue, overwriteOptionValue, verboseOptionValue) =>
        {
            MergeDatabase(sourceArgumentValue, targetArgumentValue, overwriteOptionValue, verboseOptionValue);
        },
        sourceDatabaseArgument, targetDatabaseArgument, overwriteOption, verboseOption);

        return mergeDatabaseCommand;
    }

    public static void MergeDatabase(string sourceFile, string targetFile, bool overwriteProviders, bool verbose)
    {
        foreach (var path in new[] { sourceFile, targetFile })
        {
            if (!File.Exists(path))
            {
                Console.WriteLine($"File not found: {path}");
                return;
            }
        }

        var sourceProviders = new List<ProviderDetails>();

        using (var sourceContext = new EventProviderDbContext(sourceFile, readOnly: true))
        {
            sourceProviders.AddRange(sourceContext.ProviderDetails.ToList());
        }

        using var targetContext = new EventProviderDbContext(targetFile, readOnly: false);

        var providersAlreadyInTarget = new Dictionary<string, ProviderDetails>();

        foreach (var sourceProviderDetails in sourceProviders)
        {
            var existingProviderInTarget = targetContext.ProviderDetails.FirstOrDefault(p => p.ProviderName == sourceProviderDetails.ProviderName);
            if (existingProviderInTarget != null)
            {
                providersAlreadyInTarget.Add(existingProviderInTarget.ProviderName, existingProviderInTarget);
            }
        }

        if (providersAlreadyInTarget.Count > 0)
        {
            Console.WriteLine($"The target database contains {providersAlreadyInTarget.Count} providers that are in the source.");
            if (overwriteProviders)
            {
                Console.WriteLine("Removing these providers from the target database...");

                foreach (var provider in providersAlreadyInTarget.Values)
                {
                    targetContext.Remove(provider);
                }

                targetContext.SaveChanges();
                Console.WriteLine($"Removal of {providersAlreadyInTarget.Count} completed.");
            }
            else
            {
                Console.WriteLine("These providers will not be copied from the source.");
            }
        }

        Console.WriteLine("Copying providers from the source...");

        var providersCopied = new List<ProviderDetails>();

        foreach (var provider in sourceProviders)
        {
            if (providersAlreadyInTarget.ContainsKey(provider.ProviderName) && !overwriteProviders)
            {
                Console.WriteLine($"Skipping provider: {provider.ProviderName}");
                continue;
            }

            targetContext.ProviderDetails.Add(new ProviderDetails
            {
                ProviderName = provider.ProviderName,
                Events = provider.Events,
                Parameters = provider.Parameters,
                Keywords = provider.Keywords,
                Messages = provider.Messages,
                Opcodes = provider.Opcodes,
                Tasks = provider.Tasks
            });

            providersCopied.Add(provider);
        }

        targetContext.SaveChanges();

        Console.WriteLine("Providers copied:");
        Console.WriteLine();
        LogProviderDetailHeader(providersCopied.Select(p => p.ProviderName));
        foreach (var provider in providersCopied)
        {
            LogProviderDetails(provider);
        }
    }
}
