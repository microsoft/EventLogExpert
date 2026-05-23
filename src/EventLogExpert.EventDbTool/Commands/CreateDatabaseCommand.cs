// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Contracts;
using EventLogExpert.DatabaseTools.Operations;
using EventLogExpert.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System.CommandLine;

namespace EventLogExpert.EventDbTool.Commands;

public sealed class CreateDatabaseCommand
{
    public static Command GetCommand()
    {
        Command createDatabaseCommand = new("create", "Creates a new event database.");

        Argument<string> fileArgument = new("file")
        {
            Description = "File to create. Must have a .db extension."
        };

        Argument<string?> sourceArgument = new("source")
        {
            Description = "Optional provider source: a .db file, an exported .evtx file, or a folder containing " +
                ".db and/or .evtx files (top-level only). When omitted, local providers on this machine are used. " +
                "When supplied, ONLY the source is used (no fallback to local providers).",
            Arity = ArgumentArity.ZeroOrOne
        };

        Option<string> filterOption = new("--filter")
        {
            Description = "Only providers matching specified regex string will be added to the database."
        };

        Option<string> skipProvidersInFileOption = new("--skip-providers-in-file")
        {
            Description =
                "Any providers found in the specified source (a .db file, an exported .evtx file, or a folder " +
                "containing them, top-level only) will not be included in the new database. " +
                "For example, when creating a database of event providers for Exchange Server, it may be useful " +
                "to provide a database of all providers from a fresh OS install with no other products. That way, all the " +
                "OS providers are skipped, and only providers added by Exchange or other installed products " +
                "would be saved in the new database."
        };

        Option<bool> verboseOption = new("--verbose")
        {
            Description = "Enable verbose logging. May be useful for troubleshooting."
        };

        createDatabaseCommand.Arguments.Add(fileArgument);
        createDatabaseCommand.Arguments.Add(sourceArgument);
        createDatabaseCommand.Options.Add(filterOption);
        createDatabaseCommand.Options.Add(skipProvidersInFileOption);
        createDatabaseCommand.Options.Add(verboseOption);

        createDatabaseCommand.SetAction(async result =>
        {
            await using var sp = Program.BuildServiceProvider(result.GetValue(verboseOption));
            var logger = sp.GetRequiredService<ITraceLogger>();

            if (!RegexHelper.TryCreate(result.GetValue(filterOption), logger, out var regex)) { return; }

            var request = new CreateDatabaseRequest(
                result.GetRequiredValue(fileArgument),
                result.GetValue(sourceArgument),
                regex,
                result.GetValue(skipProvidersInFileOption));

            await new CreateDatabaseOperation(request).ExecuteAsync(logger, progress: null, CancellationToken.None);
        });

        return createDatabaseCommand;
    }
}
