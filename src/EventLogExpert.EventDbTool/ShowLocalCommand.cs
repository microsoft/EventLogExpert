// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Providers;
using Microsoft.Extensions.DependencyInjection;
using System.CommandLine;

namespace EventLogExpert.EventDbTool;

public class ShowLocalCommand(ITraceLogger logger) : DbToolCommand(logger)
{
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
        {
            using var sp = Program.BuildServiceProvider(action.GetValue(verboseOption));
            new ShowLocalCommand(sp.GetRequiredService<ITraceLogger>())
                .ShowProviderInfo(action.GetValue(filterOption));
        });

        return showProvidersCommand;
    }

    private void ShowProviderInfo(string? filter)
    {
        var providerNames = GetLocalProviderNames(filter);

        LogProviderDetailHeader(providerNames);

        foreach (var providerName in providerNames)
        {
            var provider = new EventMessageProvider(providerName, Logger);
            var details = provider.LoadProviderDetails();

            LogProviderDetails(details);
        }
    }
}
