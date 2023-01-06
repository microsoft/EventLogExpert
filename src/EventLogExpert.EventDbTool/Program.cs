using System.CommandLine;

namespace EventLogExpert.EventDbTool;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Tool used to create and modify databases for use with EventLogExpert");

        rootCommand.AddCommand(ShowProvidersCommand.GetCommand());

        rootCommand.AddCommand(CreateDatabaseCommand.GetCommand());

        return await rootCommand.InvokeAsync(args);
    }
}
