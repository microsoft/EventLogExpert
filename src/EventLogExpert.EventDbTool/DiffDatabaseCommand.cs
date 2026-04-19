// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventProviderDatabase;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Providers;
using Microsoft.Extensions.DependencyInjection;
using System.CommandLine;

namespace EventLogExpert.EventDbTool;

public class DiffDatabaseCommand(ITraceLogger logger) : DbToolCommand(logger)
{
    public static Command GetCommand()
    {
        Command diffDatabaseCommand = new(
            "diff",
            "Given two provider sources (each may be a .db, an exported .evtx, or a folder containing them), " +
            "produces a database containing all providers from the second source which are not in the first source.");

        Argument<string> firstArgument = new("first source")
        {
            Description = "The first source to compare: a .db, an exported .evtx, or a folder containing .db and/or .evtx files (top-level only)."
        };

        Argument<string> secondArgument = new("second source")
        {
            Description = "The second source to compare: a .db, an exported .evtx, or a folder containing .db and/or .evtx files (top-level only)."
        };

        Argument<string> newDbArgument = new("new db")
        {
            Description = "The new database containing only the providers in the second source which are not in the first source. Must have a .db extension."
        };

        Option<bool> verboseOption = new("--verbose")
        {
            Description = "Verbose logging. May be useful for troubleshooting."
        };

        diffDatabaseCommand.Arguments.Add(firstArgument);
        diffDatabaseCommand.Arguments.Add(secondArgument);
        diffDatabaseCommand.Arguments.Add(newDbArgument);
        diffDatabaseCommand.Options.Add(verboseOption);

        diffDatabaseCommand.SetAction(action =>
        {
            using var sp = Program.BuildServiceProvider(action.GetValue(verboseOption));
            new DiffDatabaseCommand(sp.GetRequiredService<ITraceLogger>())
                .DiffDatabase(
                    action.GetRequiredValue(firstArgument),
                    action.GetRequiredValue(secondArgument),
                    action.GetRequiredValue(newDbArgument));
        });

        return diffDatabaseCommand;
    }

    private void DiffDatabase(string firstSource, string secondSource, string newDb)
    {
        if (!ProviderSource.TryValidate(firstSource, Logger)) { return; }
        if (!ProviderSource.TryValidate(secondSource, Logger)) { return; }

        if (File.Exists(newDb))
        {
            Logger.Error($"File already exists: {newDb}");
            return;
        }

        if (!string.Equals(Path.GetExtension(newDb), ".db", StringComparison.OrdinalIgnoreCase))
        {
            Logger.Error($"New db path must have a .db extension.");
            return;
        }

        var firstProviderNames = new HashSet<string>(
            ProviderSource.LoadProviderNames(firstSource, Logger),
            StringComparer.OrdinalIgnoreCase);

        var providersCopied = new List<ProviderDetails>();

        // Pass firstProviderNames as the skip set so providers present in the first source are
        // never resolved from the second source's metadata path. This is especially important when
        // the second source is .evtx+MTA, where each provider triggers an expensive load.
        Logger.Info($"Skipping up to {firstProviderNames.Count} provider name(s) from the second source that also appear in the first source.");

        // Defer creating the DbContext (and therefore the .db file on disk) until at least one
        // provider is actually about to be persisted. This prevents leaving an empty database
        // behind when the second source yields no new providers.
        EventProviderDbContext? newDbContext = null;

        try
        {
            foreach (var details in ProviderSource.LoadProviders(secondSource, Logger, filter: null, skipProviderNames: firstProviderNames))
            {
                Logger.Info($"Copying {details.ProviderName} because it is present in second source but not first.");

                newDbContext ??= new EventProviderDbContext(newDb, false, Logger);

                newDbContext.ProviderDetails.Add(new ProviderDetails
                {
                    ProviderName = details.ProviderName,
                    Events = details.Events,
                    Parameters = details.Parameters,
                    Keywords = details.Keywords,
                    Messages = details.Messages,
                    Opcodes = details.Opcodes,
                    Tasks = details.Tasks
                });

                providersCopied.Add(details);
            }

            if (newDbContext is null)
            {
                Logger.Warn($"No providers in the second source are missing from the first. Database was not created.");
                return;
            }

            newDbContext.SaveChanges();

            Logger.Info($"Providers copied to new database:");
            Logger.Info($"");
            LogProviderDetailHeader(providersCopied.Select(p => p.ProviderName));

            foreach (var provider in providersCopied)
            {
                LogProviderDetails(provider);
            }
        }
        finally
        {
            newDbContext?.Dispose();
        }
    }
}
