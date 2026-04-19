// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventProviderDatabase;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Providers;
using Microsoft.EntityFrameworkCore;
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

        Argument<string> sourceArgument = new("source")
        {
            Description = "The provider source: a .db file, an exported .evtx file, or a folder containing .db and/or .evtx files (top-level only)."
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

        mergeDatabaseCommand.Arguments.Add(sourceArgument);
        mergeDatabaseCommand.Arguments.Add(targetDatabaseArgument);
        mergeDatabaseCommand.Options.Add(overwriteOption);
        mergeDatabaseCommand.Options.Add(verboseOption);

        mergeDatabaseCommand.SetAction(action =>
        {
            using var sp = Program.BuildServiceProvider(action.GetValue(verboseOption));
            new MergeDatabaseCommand(sp.GetRequiredService<ITraceLogger>())
                .MergeDatabase(
                    action.GetRequiredValue(sourceArgument),
                    action.GetRequiredValue(targetDatabaseArgument),
                    action.GetValue(overwriteOption));
        });

        return mergeDatabaseCommand;
    }

    private void MergeDatabase(string source, string targetFile, bool overwriteProviders)
    {
        if (!ProviderSource.TryValidate(source, Logger)) { return; }

        if (!File.Exists(targetFile))
        {
            Logger.Error($"File not found: {targetFile}");
            return;
        }

        var sourceProviders = ProviderSource.LoadProviders(source, Logger).ToList();

        if (sourceProviders.Count == 0)
        {
            Logger.Warn($"No providers were discovered in the source.");
            return;
        }

        using var targetContext = new EventProviderDbContext(targetFile, false, Logger);

        // Pre-load all target ProviderName values once (cheap projection), then compute the case-
        // insensitive overlap with the source in-memory. This replaces a previous N+1 query that
        // issued one Where(...).ToList() per source provider against the target table.
        var sourceNames = new HashSet<string>(
            sourceProviders.Select(p => p.ProviderName),
            StringComparer.OrdinalIgnoreCase);

        var targetMatchingNames = targetContext.ProviderDetails
            .AsNoTracking()
            .Select(p => p.ProviderName)
            .ToList()
            .Where(n => sourceNames.Contains(n))
            .ToList();

        // Track the source-side provider names whose case-insensitive equivalent exists in target.
        // ProviderName is the primary key in the target DB, so case-sensitive uniqueness identifies
        // a row; the case-insensitive HashSet drives the no-overwrite skip check on the source side.
        var providerNamesInTarget = new HashSet<string>(targetMatchingNames, StringComparer.OrdinalIgnoreCase);

        if (targetMatchingNames.Count > 0)
        {
            Logger.Info($"The target database contains {targetMatchingNames.Count} provider row(s) matching {providerNamesInTarget.Count} provider name(s) in the source.");

            if (overwriteProviders)
            {
                Logger.Info($"Removing these providers from the target database...");

                // Single round-trip to load just the rows we need to remove. Since these names came
                // from the same DB, exact (binary) matching here is correct.
                var toRemove = targetContext.ProviderDetails
                    .Where(p => targetMatchingNames.Contains(p.ProviderName))
                    .ToList();

                targetContext.RemoveRange(toRemove);
                targetContext.SaveChanges();
                Logger.Info($"Removal of {toRemove.Count} provider row(s) completed.");
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
            if (providerNamesInTarget.Contains(provider.ProviderName) && !overwriteProviders)
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
