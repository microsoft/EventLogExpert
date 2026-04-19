// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Providers;
using Microsoft.Extensions.DependencyInjection;
using System.CommandLine;

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
        IEnumerable<ProviderDetails> providers;

        if (source is null)
        {
            providers = LoadLocalProviders(filter);
        }
        else
        {
            if (!ProviderSource.TryValidate(source, Logger)) { return; }

            providers = ProviderSource.LoadProviders(source, Logger, filter);
        }

        var materialized = providers
            .OrderBy(p => p.ProviderName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (materialized.Count == 0)
        {
            Logger.Warn($"No providers found.");
            return;
        }

        LogProviderDetailHeader(materialized.Select(p => p.ProviderName));

        foreach (var details in materialized)
        {
            LogProviderDetails(details);
        }
    }
}
