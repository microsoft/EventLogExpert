// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Contracts;
using EventLogExpert.DatabaseTools.Operations;
using EventLogExpert.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System.CommandLine;

namespace EventLogExpert.EventDbTool.Commands;

public sealed class MergeDatabaseCommand
{
    public static Command GetCommand()
    {
        Command mergeDatabaseCommand = new(
            "merge",
            "Copies providers from a source into a target database.");

        Argument<string> sourceArgument = new("source")
        {
            Description = "The provider source: a .db file, an exported .evtx file, or a folder containing .db and/or .evtx files (top-level only)."
        };

        Argument<string> targetDatabaseArgument = new("target db")
        {
            Description = "The target database."
        };

        Option<bool> overwriteOption = new("--overwrite")
        {
            Description = "When a provider from the source already exists in the target, overwrite the target" +
                " data with the source data. The default is to skip providers that already exist."
        };

        Option<bool> verboseOption = new("--verbose")
        {
            Description = "Enable verbose logging. May be useful for troubleshooting."
        };

        mergeDatabaseCommand.Arguments.Add(sourceArgument);
        mergeDatabaseCommand.Arguments.Add(targetDatabaseArgument);
        mergeDatabaseCommand.Options.Add(overwriteOption);
        mergeDatabaseCommand.Options.Add(verboseOption);

        mergeDatabaseCommand.SetAction(async action =>
        {
            await using var sp = Program.BuildServiceProvider(action.GetValue(verboseOption));
            var logger = sp.GetRequiredService<ITraceLogger>();

            var request = new MergeDatabaseRequest(
                action.GetRequiredValue(sourceArgument),
                action.GetRequiredValue(targetDatabaseArgument),
                action.GetValue(overwriteOption));

            await new MergeDatabaseOperation(request).ExecuteAsync(logger, progress: null, CancellationToken.None);
        });

        return mergeDatabaseCommand;
    }
}
