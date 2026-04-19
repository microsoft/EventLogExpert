// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Providers;
using Microsoft.Extensions.DependencyInjection;
using System.CommandLine;
using System.Text.RegularExpressions;

namespace EventLogExpert.EventDbTool;

public class ShowCommand(ITraceLogger logger) : DbToolCommand(logger)
{
    public static Command GetCommand()
    {
        Command showCommand = new(
            name: "show",
            description: "List event providers. When no source is supplied, lists providers on the local machine. " +
                "When a source is supplied, it may be a .db file created with this tool, an exported .evtx file " +
                "(resolved via its sibling LocaleMetaData/*.MTA files), or a folder containing either.");

        Argument<string?> sourceArgument = new("source")
        {
            Description = "Optional source: a .db file, an exported .evtx file, or a folder containing .db and/or " +
                ".evtx files (top-level only). When omitted, local providers on this machine are listed.",
            Arity = ArgumentArity.ZeroOrOne
        };

        Option<string> filterOption = new("--filter")
        {
            Description = "Filter for provider names matching the specified regex string."
        };

        Option<bool> verboseOption = new("--verbose")
        {
            Description = "Verbose logging. May be useful for troubleshooting."
        };

        showCommand.Arguments.Add(sourceArgument);
        showCommand.Options.Add(filterOption);
        showCommand.Options.Add(verboseOption);

        showCommand.SetAction(action =>
        {
            using var sp = Program.BuildServiceProvider(action.GetValue(verboseOption));
            new ShowCommand(sp.GetRequiredService<ITraceLogger>())
                .ShowProviderInfo(
                    action.GetValue(sourceArgument),
                    action.GetValue(filterOption));
        });

        return showCommand;
    }

    private void ShowProviderInfo(string? source, string? filter)
    {
        if (!RegexHelper.TryCreate(filter, Logger, out var regex)) { return; }

        try
        {
            IReadOnlyList<string> providerNames;
            IEnumerable<ProviderDetails> providers;

            if (source is null)
            {
                providerNames = GetLocalProviderNames(regex);
                providers = LoadLocalProviders(regex);
            }
            else
            {
                if (!ProviderSource.TryValidate(source, Logger)) { return; }

                providerNames = ProviderSource.LoadProviderNames(source, Logger, regex);
                providers = ProviderSource.LoadProviders(source, Logger, regex);
            }

            if (providerNames.Count == 0)
            {
                Logger.Warn($"No providers found.");
                return;
            }

            LogProviderDetailHeader(providerNames);

            foreach (var details in providers)
            {
                LogProviderDetails(details);
            }
        }
        catch (RegexMatchTimeoutException)
        {
            Logger.Error($"The --filter regex timed out. The pattern may cause catastrophic backtracking.");
        }
    }
}
