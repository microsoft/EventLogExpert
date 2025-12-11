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
        Command showProvidersCommand = new(
            "showlocal",
            "List the event providers on the local machine.");

        Option<string> filterOption = new("--filter")
        {
            Description = "Filter for provider names matching the specified regex string."
        };

        Option<bool> verboseOption = new("--verbose")
        {
            Description = "Verbose logging. May be useful for troubleshooting."
        };

        showProvidersCommand.Options.Add(filterOption);
        showProvidersCommand.Options.Add(verboseOption);

        showProvidersCommand.SetAction(action =>
            ShowProviderInfo(action.GetValue(filterOption), action.GetValue(verboseOption)));

        return showProvidersCommand;
    }

    private static void ShowProviderInfo(string? filter, bool verbose)
    {
        var providerNames = GetLocalProviderNames(filter);

        LogProviderDetailHeader(providerNames);

        foreach (var providerName in providerNames)
        {
            var provider = new EventMessageProvider(providerName, verbose ? s_logger : null);
            var details = provider.LoadProviderDetails();

            LogProviderDetails(details);
        }
    }
}
