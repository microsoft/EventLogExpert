// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.CommandLine;

namespace EventLogExpert.EventDbTool;

internal class Program
{
    internal static ServiceProvider BuildServiceProvider(bool verbose) =>
        new ServiceCollection()
            .AddSingleton<ITraceLogger>(new TraceLogger(verbose ? LogLevel.Trace : LogLevel.Information))
            .BuildServiceProvider();

    private static async Task<int> Main(string[] args)
    {
        RootCommand rootCommand = new("Tool used to create and modify databases for use with EventLogExpert");

        rootCommand.Subcommands.Add(ShowCommand.GetCommand());
        rootCommand.Subcommands.Add(CreateDatabaseCommand.GetCommand());
        rootCommand.Subcommands.Add(MergeDatabaseCommand.GetCommand());
        rootCommand.Subcommands.Add(DiffDatabaseCommand.GetCommand());
        rootCommand.Subcommands.Add(UpgradeDatabaseCommand.GetCommand());

        return await rootCommand.Parse(args).InvokeAsync();
    }
}
