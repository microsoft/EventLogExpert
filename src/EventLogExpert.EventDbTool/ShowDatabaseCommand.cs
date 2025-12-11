// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventProviderDatabase;
using System.CommandLine;
using System.Text.RegularExpressions;

namespace EventLogExpert.EventDbTool;

public class ShowDatabaseCommand : DbToolCommand
{
    public static Command GetCommand()
    {
        Command showDatabaseCommand = new (name: "showdatabase", description: "List the event providers from a database created with this tool.");

        Argument<string> fileArgument = new("file")
        {
            Description = "The database file to show."
        };

        Option<string> filterOption = new("--filter")
        {
            Description = "Filter for provider names matching the specified regex string."
        };

        Option<bool> verboseOption = new("--verbose")
        {
            Description = "Verbose logging. May be useful for troubleshooting."
        };

        showDatabaseCommand.Arguments.Add(fileArgument);
        showDatabaseCommand.Options.Add(filterOption);
        showDatabaseCommand.Options.Add(verboseOption);

        showDatabaseCommand.SetAction(action => ShowProviderInfo(
            action.GetRequiredValue(fileArgument),
            action.GetValue(filterOption),
            action.GetValue(verboseOption)));

        return showDatabaseCommand;
    }

    private static void ShowProviderInfo(string file, string? filter, bool verbose)
    {
        if (!File.Exists(file))
        {
            Console.WriteLine($"File not found: {file}");
            return;
        }

        using var dbContext = new EventProviderDbContext(file, readOnly: true);

        var providerNames = dbContext.ProviderDetails.Select(p => p.ProviderName).OrderBy(name => name).ToList();

        if (!string.IsNullOrEmpty(filter))
        {
            var regex = new Regex(filter);
            providerNames = providerNames.Where(p => regex.IsMatch(p)).ToList();
        }

        LogProviderDetailHeader(providerNames);

        foreach (var name in providerNames)
        {
            LogProviderDetails(dbContext.ProviderDetails.First(p => p.ProviderName == name));
        }
    }
}
