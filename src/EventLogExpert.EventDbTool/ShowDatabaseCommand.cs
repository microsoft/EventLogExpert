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
        var showDatabaseCommand = new Command(
            name: "showdatabase",
            description: "List the event providers from a database created with this tool.");
        var fileArgument = new Argument<string>(
            name: "file",
            description: "The database file to show.");
        var filterOption = new Option<string>(
            name: "--filter",
            description: "Filter for provider names matching the specified regex string.");
        var verboseOption = new Option<bool>(
            name: "--verbose",
            description: "Verbose logging. May be useful for troubleshooting.");
        showDatabaseCommand.AddArgument(fileArgument);
        showDatabaseCommand.AddOption(filterOption);
        showDatabaseCommand.AddOption(verboseOption);
        showDatabaseCommand.SetHandler(ShowProviderInfo, fileArgument, filterOption, verboseOption);

        return showDatabaseCommand;
    }

    public static void ShowProviderInfo(string file, string filter, bool verbose)
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
