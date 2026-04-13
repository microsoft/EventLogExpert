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
            "Given two databases, produces a third database containing all providers " +
            "from the second database which are not in the first database.");

        Argument<string> dbOneArgument = new("first db")
        {
            Description = "The first database to compare."
        };

        Argument<string> dbTwoArgument = new("second db")
        {
            Description = "The second database to compare."
        };

        Argument<string> newDbArgument = new("new db")
        {
            Description = "The new database containing only the providers in the second db which are not in the first db. Must have a .db extension."
        };

        Option<bool> verboseOption = new("--verbose")
        {
            Description = "Verbose logging. May be useful for troubleshooting."
        };

        diffDatabaseCommand.Arguments.Add(dbOneArgument);
        diffDatabaseCommand.Arguments.Add(dbTwoArgument);
        diffDatabaseCommand.Arguments.Add(newDbArgument);
        diffDatabaseCommand.Options.Add(verboseOption);

        diffDatabaseCommand.SetAction(action =>
        {
            using var sp = Program.BuildServiceProvider(action.GetValue(verboseOption));
            new DiffDatabaseCommand(sp.GetRequiredService<ITraceLogger>())
                .DiffDatabase(
                    action.GetRequiredValue(dbOneArgument),
                    action.GetRequiredValue(dbTwoArgument),
                    action.GetRequiredValue(newDbArgument));
        });

        return diffDatabaseCommand;
    }

    private void DiffDatabase(string dbOne, string dbTwo, string newDb)
    {
        foreach (var path in new[] { dbOne, dbTwo })
        {
            if (File.Exists(path)) { continue; }

            Logger.Error($"File not found: {path}");
            return;
        }

        if (File.Exists(newDb))
        {
            Logger.Error($"File already exists: {newDb}");
            return;
        }

        if (Path.GetExtension(newDb) != ".db")
        {
            Logger.Error($"New db path must have a .db extension.");
            return;
        }

        var dbOneProviderNames = new HashSet<string>();

        using (var dbOneContext = new EventProviderDbContext(dbOne, true, Logger))
        {
            dbOneContext.ProviderDetails.Select(p => p.ProviderName).ToList()
                .ForEach(name => dbOneProviderNames.Add(name));
        }

        var providersCopied = new List<ProviderDetails>();

        using var dbTwoContext = new EventProviderDbContext(dbTwo, true, Logger);
        using var newDbContext = new EventProviderDbContext(newDb, false, Logger);

        foreach (var details in dbTwoContext.ProviderDetails)
        {
            if (dbOneProviderNames.Contains(details.ProviderName))
            {
                Logger.Info($"Skipping {details.ProviderName} because it is present in both databases.");
            }
            else
            {
                Logger.Info($"Copying {details.ProviderName} because it is present in second db but not first db.");
                newDbContext.ProviderDetails.Add(new ProviderDetails
                {
                    ProviderName = details.ProviderName,
                    Events = details.Events,
                    Keywords = details.Keywords,
                    Messages = details.Messages,
                    Opcodes = details.Opcodes,
                    Tasks = details.Tasks
                });

                providersCopied.Add(details);
            }
        }

        newDbContext.SaveChanges();

        if (providersCopied.Count <= 0) { return; }
        
        Logger.Info($"Providers copied to new database:");
        Logger.Info($"");
        LogProviderDetailHeader(providersCopied.Select(p => p.ProviderName));

        foreach (var provider in providersCopied)
        {
            LogProviderDetails(provider);
        }
    }
}
