// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common;
using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.DatabaseTools.CreateDatabase;
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

        Option<string> offlineImageOption = new("--offline-image")
        {
            Description =
                "Build the database from a Windows image, fully offline (no host registry or host files are " +
                "read): a mounted volume root (e.g. an attached VHDX), an extracted image folder, or a .wim/.esd " +
                "file (use --wim-index N to pick the image). The kind is auto-detected from the path; override with " +
                "--image-kind. Mutually exclusive with the source argument."
        };

        Option<string> imageKindOption = new("--image-kind")
        {
            Description =
                "Override how --offline-image is read: 'directory' (a mounted volume or extracted folder) or 'wim' " +
                "(a .wim/.esd file). Omit to auto-detect from the path (a directory, or a .wim/.esd file)."
        };

        Option<int?> wimIndexOption = new("--wim-index")
        {
            Description =
                "The 1-based image index to extract from a .wim/.esd, for --image-kind wim. Omit to list the " +
                "available images."
        };

        Option<bool> verboseOption = new("--verbose")
        {
            Description = "Enable verbose logging. May be useful for troubleshooting."
        };

        createDatabaseCommand.Arguments.Add(fileArgument);
        createDatabaseCommand.Arguments.Add(sourceArgument);
        createDatabaseCommand.Options.Add(filterOption);
        createDatabaseCommand.Options.Add(skipProvidersInFileOption);
        createDatabaseCommand.Options.Add(offlineImageOption);
        createDatabaseCommand.Options.Add(imageKindOption);
        createDatabaseCommand.Options.Add(wimIndexOption);
        createDatabaseCommand.Options.Add(verboseOption);

        createDatabaseCommand.SetAction(async result =>
        {
            await using var sp = Program.BuildServiceProvider(result.GetValue(verboseOption));
            var logger = sp.GetRequiredService<ITraceLogger>();

            var filterValue = result.GetValue(filterOption);

            if (!FilterRegexFactory.TryCreate(filterValue, out var regex, out var error))
            {
                logger.Error($"Invalid --filter regex '{filterValue}': {error}");

                return;
            }

            var imageKindValue = result.GetValue(imageKindOption);
            OfflineImageKind? imageKind = null;

            if (!string.IsNullOrWhiteSpace(imageKindValue))
            {
                if (!Enum.TryParse(imageKindValue, ignoreCase: true, out OfflineImageKind parsed) ||
                    !Enum.IsDefined(parsed) ||
                    parsed == OfflineImageKind.Iso)
                {
                    logger.Error($"Invalid --image-kind '{imageKindValue}'. Valid values: directory, wim.");

                    return;
                }

                imageKind = parsed;
            }

            var request = new CreateDatabaseRequest(
                result.GetRequiredValue(fileArgument),
                result.GetValue(sourceArgument),
                regex,
                result.GetValue(skipProvidersInFileOption),
                OfflineImagePath: result.GetValue(offlineImageOption),
                ImageKind: imageKind,
                WimIndex: result.GetValue(wimIndexOption));

            var factory = sp.GetRequiredService<IDatabaseToolsOperationFactory>();

            await factory.Create(request).ExecuteAsync(logger, progress: null, CancellationToken.None);
        });

        return createDatabaseCommand;
    }
}
