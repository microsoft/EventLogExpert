// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.CommandLine;

namespace EventLogExpert.EventDbTool;

internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        RootCommand rootCommand = new("Tool used to create and modify databases for use with EventLogExpert");

        rootCommand.Subcommands.Add(ShowLocalCommand.GetCommand());
        rootCommand.Subcommands.Add(ShowDatabaseCommand.GetCommand());
        rootCommand.Subcommands.Add(CreateDatabaseCommand.GetCommand());
        rootCommand.Subcommands.Add(MergeDatabaseCommand.GetCommand());
        rootCommand.Subcommands.Add(DiffDatabaseCommand.GetCommand());
        rootCommand.Subcommands.Add(UpgradeDatabaseCommand.GetCommand());

        return await rootCommand.Parse(args).InvokeAsync();
    }
}
