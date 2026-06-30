// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.DatabaseTools.UpgradeDatabase;
using EventLogExpert.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System.CommandLine;

namespace EventLogExpert.EventDbTool.Commands;

public sealed class UpgradeDatabaseCommand
{
    public static Command GetCommand()
    {
        Command upgradeDatabaseCommand = new(
            "upgrade",
            "Upgrades the database schema");

        Argument<string> fileArgument = new("file")
        {
            Description = "The database file to upgrade."
        };

        Option<bool> verboseOption = new("--verbose")
        {
            Description = "Verbose logging. May be useful for troubleshooting."
        };

        upgradeDatabaseCommand.Arguments.Add(fileArgument);
        upgradeDatabaseCommand.Options.Add(verboseOption);

        upgradeDatabaseCommand.SetAction(async (action, cancellationToken) =>
        {
            await using var sp = Program.BuildServiceProvider(action.GetValue(verboseOption));
            var logger = sp.GetRequiredService<ITraceLogger>();

            var request = new UpgradeDatabaseRequest(action.GetRequiredValue(fileArgument));

            var factory = sp.GetRequiredService<IDatabaseToolsOperationFactory>();

            var outcome = await factory.Create(request).ExecuteAsync(logger, progress: null, cancellationToken);

            return CommandExitCode.ToExitCode(outcome);
        });

        return upgradeDatabaseCommand;
    }
}
