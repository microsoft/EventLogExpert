﻿using System.CommandLine;

namespace EventLogExpert.EventDbTool;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Tool used to create and modify databases for use with EventLogExpert");

        rootCommand.AddCommand(ShowLocalCommand.GetCommand());

        rootCommand.AddCommand(ShowDatabaseCommand.GetCommand());

        rootCommand.AddCommand(CreateDatabaseCommand.GetCommand());

        rootCommand.AddCommand(MergeDatabaseCommand.GetCommand());

        rootCommand.AddCommand(DiffDatabaseCommand.GetCommand());

        return await rootCommand.InvokeAsync(args);
    }
}
