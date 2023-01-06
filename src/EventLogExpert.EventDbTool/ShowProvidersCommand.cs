// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Providers;
using System.CommandLine;
using System.Diagnostics.Eventing.Reader;
using System.Text.RegularExpressions;

namespace EventLogExpert.EventDbTool;

public class ShowProvidersCommand : DbToolCommand
{
    public static Command GetCommand()
    {
        var showProvidersCommand = new Command(
            name: "showproviders",
            description: "List the event providers on the local machine.");
        var detailedOption = new Option<bool>(
            name: "--detailed",
            description: "Include details such as the number of events and messages.");
        var filterOption = new Option<string>(
            name: "--filter",
            description: "Filter for provider names matching the specified regex string.");
        showProvidersCommand.AddOption(detailedOption);
        showProvidersCommand.AddOption(filterOption);
        showProvidersCommand.SetHandler((detailedOptionValue, filterOptionValue) =>
        {
            ShowProviderInfo(detailedOptionValue, filterOptionValue);
        },
        detailedOption, filterOption);

        return showProvidersCommand;
    }

    public static void ShowProviderInfo(bool detailed, string filter)
    {
        var providerNames = GetProviderNames(filter);

        if (!detailed)
        {
            foreach (var provider in providerNames)
            {
                Console.WriteLine(provider);
            }
        }
        else
        {
            LogProviderDetailHeader(providerNames);
            foreach (var providerName in providerNames)
            {
                var provider = new EventMessageProvider(providerName, s => { });
                var details = provider.LoadProviderDetails();
                LogProviderDetails(details);

                details = null;
            }
        }
    }
}
