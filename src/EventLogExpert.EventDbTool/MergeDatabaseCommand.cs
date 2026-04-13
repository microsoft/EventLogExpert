// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventProviderDatabase;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Providers;
using Microsoft.Extensions.DependencyInjection;
using System.CommandLine;

namespace EventLogExpert.EventDbTool;

public class MergeDatabaseCommand(ITraceLogger logger) : DbToolCommand(logger)
{
    public static Command GetCommand()
    {
        Command mergeDatabaseCommand = new(
            "merge",
            "Copies providers from a source into a target database.");

        Argument<string> sourceDatabaseArgument = new("source db")
        {
            Description = "The source database from which to copy providers."
        };

        Argument<string> targetDatabaseArgument = new("target db")
        {
            Description = "The target database."
        };

        Option<bool> overwriteOption = new("--overwrite")
        {
            Description = "When a provider from the source already exists in the target, overwrite the target" +
                " data with the source data. The default is to skip providers that already exist."
        };

        Option<bool> verboseOption = new("--verbose")
        {
            Description = "Enable verbose logging. May be useful for troubleshooting."
        };

        mergeDatabaseCommand.Arguments.Add(sourceDatabaseArgument);
        mergeDatabaseCommand.Arguments.Add(targetDatabaseArgument);
        mergeDatabaseCommand.Options.Add(overwriteOption);
        mergeDatabaseCommand.Options.Add(verboseOption);

        mergeDatabaseCommand.SetAction(action =>
        {
            using var sp = Program.BuildServiceProvider(action.GetValue(verboseOption));
            new MergeDatabaseCommand(sp.GetRequiredService<ITraceLogger>())
                .MergeDatabase(
                    action.GetRequiredValue(sourceDatabaseArgument),
                    action.GetRequiredValue(targetDatabaseArgument),
                    action.GetValue(overwriteOption));
        });

        return mergeDatabaseCommand;
    }

    private void MergeDatabase(string sourceFile, string targetFile, bool overwriteProviders)
    {
        foreach (var path in new[] { sourceFile, targetFile })
        {
            if (File.Exists(path)) { continue; }

            Logger.Error($"File not found: {path}");
            return;
        }

        var sourceProviders = new List<ProviderDetails>();

        using (var sourceContext = new EventProviderDbContext(sourceFile, true, Logger))
        {
            sourceProviders.AddRange(sourceContext.ProviderDetails.ToList());
        }

        using var targetContext = new EventProviderDbContext(targetFile, false, Logger);

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
            Logger.Info($"The target database contains {providersAlreadyInTarget.Count} providers that are in the source.");

            if (overwriteProviders)
            {
                Logger.Info($"Removing these providers from the target database...");

                foreach (var provider in providersAlreadyInTarget.Values)
                {
                    targetContext.Remove(provider);
                }

                targetContext.SaveChanges();
                Logger.Info($"Removal of {providersAlreadyInTarget.Count} completed.");
            }
            else
            {
                Logger.Info($"These providers will not be copied from the source.");
            }
        }

        Logger.Info($"Copying providers from the source...");

        var providersCopied = new List<ProviderDetails>();

        foreach (var provider in sourceProviders)
        {
            if (providersAlreadyInTarget.ContainsKey(provider.ProviderName) && !overwriteProviders)
            {
                Logger.Info($"Skipping provider: {provider.ProviderName}");
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

        Logger.Info($"Providers copied:");
        Logger.Info($"");
        LogProviderDetailHeader(providersCopied.Select(p => p.ProviderName));

        foreach (var provider in providersCopied)
        {
            LogProviderDetails(provider);
        }
    }
}
