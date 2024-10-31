// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventProviderDatabase;
using EventLogExpert.Eventing.Providers;
using System.CommandLine;

namespace EventLogExpert.EventDbTool;

public class DiffDatabaseCommand : DbToolCommand
{
    public static Command GetCommand()
    {
        var diffDatabaseCommand = new Command(
            name: "diff",
            description: "Given two databases, produces a third database containing all providers " +
                "from the second database which are not in the first database.");
        var dbOneArgument = new Argument<string>(
            name: "first db",
            description: "The first database to compare.");
        var dbTwoArgument = new Argument<string>(
            name: "second db",
            description: "The second database to compare.");
        var newDbArgument = new Argument<string>(
            name: "new db",
            description: "The new database containing only the providers in the second db which are not in the first db. Must have a .db extension.");
        var verboseOption = new Option<bool>(
            name: "--verbose",
            description: "Verbose logging. May be useful for troubleshooting.");
        diffDatabaseCommand.AddArgument(dbOneArgument);
        diffDatabaseCommand.AddArgument(dbTwoArgument);
        diffDatabaseCommand.AddArgument(newDbArgument);
        diffDatabaseCommand.AddOption(verboseOption);
        diffDatabaseCommand.SetHandler(DiffDatabase, dbOneArgument, dbTwoArgument, newDbArgument, verboseOption);

        return diffDatabaseCommand;
    }

    public static void DiffDatabase(string dbOne, string dbTwo, string newDb, bool verbose)
    {
        foreach (var path in new[] { dbOne, dbTwo })
        {
            if (!File.Exists(path))
            {
                Console.WriteLine($"File not found: {path}");
                return;
            }
        }

        if (File.Exists(newDb))
        {
            Console.WriteLine($"File already exists: {newDb}");
            return;
        }

        if (Path.GetExtension(newDb) != ".db")
        {
            Console.WriteLine($"New db path must have a .db extension.");
            return;
        }

        var dbOneProviderNames = new HashSet<string>();

        using (var dbOneContext = new EventProviderDbContext(dbOne, readOnly: true))
        {
            dbOneContext.ProviderDetails.Select(p => p.ProviderName).ToList().ForEach(name => dbOneProviderNames.Add(name));
        }

        var providersCopied = new List<ProviderDetails>();

        using var dbTwoContext = new EventProviderDbContext(dbTwo, readOnly: true);
        using var newDbContext = new EventProviderDbContext(newDb, readOnly: false);

        foreach (var details in dbTwoContext.ProviderDetails)
        {
            if (dbOneProviderNames.Contains(details.ProviderName))
            {
                if (verbose) Console.WriteLine($"Skipping {details.ProviderName} because it is present in both databases.");
                continue;
            }
            else
            {
                if (verbose) Console.WriteLine($"Copying {details.ProviderName} because it is present in second db but not first db.");

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

        if (providersCopied.Count > 0)
        {
            Console.WriteLine("Providers copied to new database:");
            Console.WriteLine();
            LogProviderDetailHeader(providersCopied.Select(p => p.ProviderName));
            foreach (var provider in providersCopied)
            {
                LogProviderDetails(provider);
            }
        }
    }
}
