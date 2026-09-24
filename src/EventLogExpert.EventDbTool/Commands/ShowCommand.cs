// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common;
using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.DatabaseTools.ShowProviders;
using EventLogExpert.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.CommandLine;

namespace EventLogExpert.EventDbTool.Commands;

public class ShowCommand
{
    public static Command GetCommand()
    {
        Command showCommand = new(
            "show",
            "List event providers. When no source is supplied, lists providers on the local machine. " +
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

        showCommand.SetAction(async (action, cancellationToken) =>
        {
            await using var sp = Program.BuildServiceProvider(action.GetValue(verboseOption));
            var logger = sp.GetRequiredService<IOperationLog>();

            var filterValue = action.GetValue(filterOption);

            if (!FilterRegexFactory.TryCreate(filterValue, out var regex, out var error))
            {
                logger.User(LogLevel.Error,
                    new LocalizableText(DatabaseToolsLogKeys.CliInvalidFilterRegex,
                        [filterValue ?? string.Empty, error ?? string.Empty]));

                return 1;
            }

            var request = new ShowProvidersRequest(action.GetValue(sourceArgument), regex);

            var factory = sp.GetRequiredService<IDatabaseToolsOperationFactory>();

            var outcome = await factory.Create(request).ExecuteAsync(logger, progress: null, cancellationToken);

            return CommandExitCode.ToExitCode(outcome);
        });

        return showCommand;
    }
}
