// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.DependencyInjection;
using EventLogExpert.EventDbTool.Commands;
using EventLogExpert.Logging;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Loggers;
using EventLogExpert.Logging.Routing;
using EventLogExpert.Logging.Sinks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.CommandLine;
using System.Security.Principal;

namespace EventLogExpert.EventDbTool;

internal class Program
{
    internal static ServiceProvider BuildServiceProvider(bool verbose) =>
        new ServiceCollection()
            .AddSingleton(new ConsoleSink(verbose ? LogLevel.Trace : LogLevel.Information))
            .AddEventLogLocalization()
            .AddSingleton<ITraceLogger>(static sp =>
                new LogSourceFactory([sp.GetRequiredService<ConsoleSink>()])
                    .ForCategory(LogSourceFactory.DefaultCategory))
            .AddSingleton<IOperationLog>(sp =>
                new StreamingOperationLog(
                    new LocalizingLogProgress(
                        sp.GetRequiredService<INeutralTextResolver>(),
                        new SinkProgress(sp.GetRequiredService<ConsoleSink>())),
                    verbose ? LogLevel.Trace : LogLevel.Information))
            .AddDatabaseToolsServices()
            .BuildServiceProvider();

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();

        return new WindowsPrincipal(identity)
            .IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static async Task<int> Main(string[] args)
    {
        if (!IsElevated())
        {
            await Console.Error.WriteLineAsync(
                "WARNING: Running without administrator privileges. " +
                "Some provider registry operations may fail or return incomplete results.");
        }

        RootCommand rootCommand = new("Tool used to create and modify databases for use with EventLogExpert");

        rootCommand.Subcommands.Add(ShowCommand.GetCommand());
        rootCommand.Subcommands.Add(CreateDatabaseCommand.GetCommand());
        rootCommand.Subcommands.Add(MergeDatabaseCommand.GetCommand());
        rootCommand.Subcommands.Add(DiffDatabaseCommand.GetCommand());
        rootCommand.Subcommands.Add(UpgradeDatabaseCommand.GetCommand());

        return await rootCommand.Parse(args).InvokeAsync();
    }

    private sealed class SinkProgress(ConsoleSink sink) : IProgress<LogRecord>
    {
        public void Report(LogRecord value) => sink.Emit(value);
    }
}
