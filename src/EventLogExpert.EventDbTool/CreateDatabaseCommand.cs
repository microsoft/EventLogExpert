// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventProviderDatabase;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Providers;
using Microsoft.Extensions.DependencyInjection;
using System.CommandLine;
using System.Text.RegularExpressions;

namespace EventLogExpert.EventDbTool;

public sealed class CreateDatabaseCommand(ITraceLogger logger) : DbToolCommand(logger)
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

        Option<bool> verboseOption = new("--verbose")
        {
            Description = "Enable verbose logging. May be useful for troubleshooting."
        };

        createDatabaseCommand.Arguments.Add(fileArgument);
        createDatabaseCommand.Arguments.Add(sourceArgument);
        createDatabaseCommand.Options.Add(filterOption);
        createDatabaseCommand.Options.Add(skipProvidersInFileOption);
        createDatabaseCommand.Options.Add(verboseOption);

        createDatabaseCommand.SetAction(result =>
        {
            using var sp = Program.BuildServiceProvider(result.GetValue(verboseOption));
            new CreateDatabaseCommand(sp.GetRequiredService<ITraceLogger>())
                .CreateDatabase(
                    result.GetRequiredValue(fileArgument),
                    result.GetValue(sourceArgument),
                    result.GetValue(filterOption),
                    result.GetValue(skipProvidersInFileOption));
        });

        return createDatabaseCommand;
    }

    private void CreateDatabase(string path, string? source, string? filter, string? skipProvidersInFile)
    {
        if (File.Exists(path))
        {
            Logger.Error($"Cannot create database because file already exists: {path}");
            return;
        }

        if (!string.Equals(Path.GetExtension(path), ".db", StringComparison.OrdinalIgnoreCase))
        {
            Logger.Error($"File extension must be .db.");
            return;
        }

        if (!RegexHelper.TryCreate(filter, Logger, out var regex)) { return; }

        if (source is not null && !ProviderSource.TryValidate(source, Logger)) { return; }

        try
        {
            HashSet<string> skipProviderNames = new(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(skipProvidersInFile))
            {
                if (!ProviderSource.TryValidate(skipProvidersInFile, Logger)) { return; }

                foreach (var name in ProviderSource.LoadProviderNames(skipProvidersInFile, Logger))
                {
                    skipProviderNames.Add(name);
                }

                Logger.Info($"Found {skipProviderNames.Count} providers in {skipProvidersInFile}. These will not be included in the new database.");
            }

            // Stream details directly into the DbContext. For .evtx sources, scanning the file
            // is expensive, so we deliberately do NOT pre-scan provider names just to size the
            // log header — that would cause a second full pass over the .evtx. Instead we
            // buffer the first batch of resolved details, derive the header column width from
            // those names, then log the header + buffered rows and continue streaming.
            const int batchSize = 100;
            var count = 0;
            var headerLogged = false;
            var pendingForHeader = new List<ProviderDetails>(batchSize);

            // Defer creating the DbContext (and therefore the .db file on disk) until we have
            // at least one provider to persist. This prevents leaving an empty database behind
            // when no provider details could be resolved (e.g., .evtx without LocaleMetaData).
            EventProviderDbContext? dbContext = null;

            try
            {
                IEnumerable<ProviderDetails> providersToAdd = source is null
                    ? LoadLocalProviders(regex, skipProviderNames)
                    : ProviderSource.LoadProviders(source, Logger, regex, skipProviderNames);

                foreach (var details in providersToAdd)
                {
                    if (!headerLogged)
                    {
                        pendingForHeader.Add(details);

                        if (pendingForHeader.Count < batchSize) { continue; }

                        FlushHeaderAndBuffer(ref dbContext, path, pendingForHeader, ref count);
                        headerLogged = true;
                        continue;
                    }

                    dbContext ??= new EventProviderDbContext(path, false, Logger);
                    dbContext.ProviderDetails.Add(details);
                    LogProviderDetails(details);
                    count++;

                    if (count % batchSize != 0) { continue; }
                    dbContext.SaveChanges();
                    dbContext.ChangeTracker.Clear();
                }

                // Flush any buffered providers when the stream ended before the buffer filled.
                if (!headerLogged && pendingForHeader.Count > 0)
                {
                    FlushHeaderAndBuffer(ref dbContext, path, pendingForHeader, ref count);
                }

                if (dbContext is null)
                {
                    Logger.Warn($"No provider details could be resolved from the source. Database was not created.");

                    return;
                }

                Logger.Info($"");
                Logger.Info($"Saving database. Please wait...");

                dbContext.SaveChanges();

                Logger.Info($"Done!");
            }
            finally
            {
                dbContext?.Dispose();
            }
        }
        catch (RegexMatchTimeoutException)
        {
            Logger.Error($"The --filter regex timed out. The pattern may cause catastrophic backtracking.");
        }
    }

    private void FlushHeaderAndBuffer(
        ref EventProviderDbContext? dbContext,
        string path,
        List<ProviderDetails> buffer,
        ref int count)
    {
        LogProviderDetailHeader(buffer.Select(p => p.ProviderName));

        dbContext ??= new EventProviderDbContext(path, false, Logger);

        foreach (var details in buffer)
        {
            dbContext.ProviderDetails.Add(details);
            LogProviderDetails(details);
            count++;
        }

        dbContext.SaveChanges();
        dbContext.ChangeTracker.Clear();
        buffer.Clear();
    }
}
