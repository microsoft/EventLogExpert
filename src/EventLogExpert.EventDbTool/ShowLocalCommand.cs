// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Providers;
using Microsoft.Extensions.Logging;
using System.CommandLine;

namespace EventLogExpert.EventDbTool;

public class ShowLocalCommand : DbToolCommand
{
    private static readonly ITraceLogger s_logger = new TraceLogger(LogLevel.Information);

    public static Command GetCommand()
    {
        var showProvidersCommand = new Command(
            name: "showlocal",
            description: "List the event providers on the local machine.");
        var filterOption = new Option<string>(
            name: "--filter",
            description: "Filter for provider names matching the specified regex string.");
        var verboseOption = new Option<bool>(
            name: "--verbose",
            description: "Verbose logging. May be useful for troubleshooting.");
        showProvidersCommand.AddOption(filterOption);
        showProvidersCommand.AddOption(verboseOption);
        showProvidersCommand.SetHandler(ShowProviderInfo, filterOption, verboseOption);

        return showProvidersCommand;
    }

    public static void ShowProviderInfo(string filter, bool verbose)
    {
        var providerNames = GetLocalProviderNames(filter);

        LogProviderDetailHeader(providerNames);
        foreach (var providerName in providerNames)
        {
            var provider = new EventMessageProvider(providerName, verbose ? s_logger : null);
            var details = provider.LoadProviderDetails();
            if (details != null)
            {
                LogProviderDetails(details);

                details = null;
            }
        }
    }
}
