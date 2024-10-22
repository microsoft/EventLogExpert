// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

//using EventLogExpert.Eventing.Providers;
//using System.CommandLine;

//namespace EventLogExpert.EventDbTool;

//public class ShowLocalCommand : DbToolCommand
//{
//    public static Command GetCommand()
//    {
//        var showProvidersCommand = new Command(
//            name: "showlocal",
//            description: "List the event providers on the local machine.");
//        var filterOption = new Option<string>(
//            name: "--filter",
//            description: "Filter for provider names matching the specified regex string.");
//        var verboseOption = new Option<bool>(
//            name: "--verbose",
//            description: "Verbose logging. May be useful for troubleshooting.");
//        showProvidersCommand.AddOption(filterOption);
//        showProvidersCommand.AddOption(verboseOption);
//        showProvidersCommand.SetHandler((filterOptionValue, verboseOptionValue) =>
//        {
//            ShowProviderInfo(filterOptionValue, verboseOptionValue);
//        },
//        filterOption, verboseOption);

//        return showProvidersCommand;
//    }

//    public static void ShowProviderInfo(string filter, bool verbose)
//    {
//        var providerNames = GetLocalProviderNames(filter);

//        LogProviderDetailHeader(providerNames);
//        foreach (var providerName in providerNames)
//        {
//            var provider = new EventMessageProvider(providerName, verbose ? (s, log) => Console.WriteLine(s) : (s, log) => { });
//            var details = provider.LoadProviderDetails();
//            if (details != null)
//            {
//                LogProviderDetails(details);

//                details = null;
//            }
//        }
//    }
//}
