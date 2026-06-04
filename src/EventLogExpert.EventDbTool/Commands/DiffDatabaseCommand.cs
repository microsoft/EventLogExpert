// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.DatabaseTools.DiffDatabase;
using EventLogExpert.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System.CommandLine;

namespace EventLogExpert.EventDbTool.Commands;

public sealed class DiffDatabaseCommand
{
    public static Command GetCommand()
    {
        Command diffDatabaseCommand = new(
            "diff",
            "Given two provider sources (each may be a .db, an exported .evtx, or a folder containing them), " +
            "produces a database containing all providers from the second source which are not in the first source.");

        Argument<string> firstArgument = new("first source")
        {
            Description = "The first source to compare: a .db, an exported .evtx, or a folder containing .db and/or .evtx files (top-level only)."
        };

        Argument<string> secondArgument = new("second source")
        {
            Description = "The second source to compare: a .db, an exported .evtx, or a folder containing .db and/or .evtx files (top-level only)."
        };

        Argument<string> newDbArgument = new("new db")
        {
            Description = "The new database containing only the providers in the second source which are not in the first source. Must have a .db extension."
        };

        Option<bool> verboseOption = new("--verbose")
        {
            Description = "Verbose logging. May be useful for troubleshooting."
        };

        diffDatabaseCommand.Arguments.Add(firstArgument);
        diffDatabaseCommand.Arguments.Add(secondArgument);
        diffDatabaseCommand.Arguments.Add(newDbArgument);
        diffDatabaseCommand.Options.Add(verboseOption);

        diffDatabaseCommand.SetAction(async action =>
        {
            await using var sp = Program.BuildServiceProvider(action.GetValue(verboseOption));
            var logger = sp.GetRequiredService<ITraceLogger>();

            var request = new DiffDatabaseRequest(
                action.GetRequiredValue(firstArgument),
                action.GetRequiredValue(secondArgument),
                action.GetRequiredValue(newDbArgument));

            var factory = sp.GetRequiredService<IDatabaseToolsOperationFactory>();

            await factory.Create(request).ExecuteAsync(logger, progress: null, CancellationToken.None);
        });

        return diffDatabaseCommand;
    }
}
