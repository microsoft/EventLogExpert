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
                "read): a mounted volume root, an extracted image folder, a .wim/.esd file, a .iso file (use " +
                "--wim-index N to pick the image), or a .vhdx/.vhd file (auto-mounted read-only). The kind is " +
                "auto-detected from the path; override with --image-kind. Mutually exclusive with the source argument."
        };

        Option<string> imageKindOption = new("--image-kind")
        {
            Description =
                "Override how --offline-image is read: 'directory' (a mounted volume or extracted folder), 'wim' " +
                "(a .wim/.esd file), 'iso' (a Windows install ISO), or 'vhdx' (a .vhdx/.vhd disk, auto-mounted). " +
                "Omit to auto-detect from the path."
        };

        Option<int?> wimIndexOption = new("--wim-index")
        {
            Description =
                "The 1-based image index to extract from a .wim/.esd (or an ISO's install.wim), for --image-kind " +
                "wim or iso. Omit to list the available images."
        };

        Option<bool> overwriteOption = new("--overwrite")
        {
            Description =
                "Replace the target database if it already exists. The existing file is backed up to a .bak " +
                "snapshot before the rebuild and restored automatically if the rebuild fails, so a failed overwrite " +
                "never destroys the prior database. Omit to fail when the target already exists."
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
        createDatabaseCommand.Options.Add(overwriteOption);
        createDatabaseCommand.Options.Add(verboseOption);

        createDatabaseCommand.SetAction(async (result, cancellationToken) =>
        {
            await using var sp = Program.BuildServiceProvider(result.GetValue(verboseOption));
            var logger = sp.GetRequiredService<ITraceLogger>();

            var filterValue = result.GetValue(filterOption);

            if (!FilterRegexFactory.TryCreate(filterValue, out var regex, out var error))
            {
                logger.Error($"Invalid --filter regex '{filterValue}': {error}");

                return 1;
            }

            var imageKindValue = result.GetValue(imageKindOption);
            OfflineImageKind? imageKind = null;

            if (!string.IsNullOrWhiteSpace(imageKindValue))
            {
                if (!Enum.TryParse(imageKindValue, ignoreCase: true, out OfflineImageKind parsed) ||
                    !Enum.IsDefined(parsed))
                {
                    logger.Error($"Invalid --image-kind '{imageKindValue}'. Valid values: directory, wim, iso, vhdx.");

                    return 1;
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
                WimIndex: result.GetValue(wimIndexOption),
                Overwrite: result.GetValue(overwriteOption));

            var factory = sp.GetRequiredService<IDatabaseToolsOperationFactory>();

            var outcome = await factory.Create(request).ExecuteAsync(logger, progress: null, cancellationToken);

            return CommandExitCode.ToExitCode(outcome);
        });

        return createDatabaseCommand;
    }
}
