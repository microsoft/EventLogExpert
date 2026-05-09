// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Logging;
using EventLogExpert.Eventing.ProviderDatabase;
using EventLogExpert.Eventing.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.CommandLine;
using System.Data.Common;

namespace EventLogExpert.EventDbTool;

public class MergeDatabaseCommand(ITraceLogger logger) : DbToolCommand(logger)
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

        mergeDatabaseCommand.SetAction(action =>
        {
            using var sp = Program.BuildServiceProvider(action.GetValue(verboseOption));
            new MergeDatabaseCommand(sp.GetRequiredService<ITraceLogger>())
                .MergeDatabase(
                    action.GetRequiredValue(sourceArgument),
                    action.GetRequiredValue(targetDatabaseArgument),
                    action.GetValue(overwriteOption));
        });

        return mergeDatabaseCommand;
    }

    internal void MergeDatabase(string source, string targetFile, bool overwriteProviders)
    {
        if (!ProviderSource.TryValidate(source, Logger)) { return; }

        if (!File.Exists(targetFile))
        {
            Logger.Error($"File not found: {targetFile}");
            return;
        }

        // Load only the cheap projection of source provider names first. This avoids resolving
        // (and for .evtx+MTA sources, expensively materializing) provider details that will be
        // skipped because they already exist in the target.
        var sourceNames = new HashSet<string>(ProviderSource.LoadProviderNames(source, Logger), StringComparer.OrdinalIgnoreCase);

        if (sourceNames.Count == 0)
        {
            Logger.Warning($"No providers were discovered in the source.");

            return;
        }

        // Probe the target schema in its own scope so a corrupt or non-SQLite file produces a
        // friendly error instead of a stack trace.
        ProviderDatabaseSchemaState targetState;

        try
        {
            using var probe = new ProviderDbContext(targetFile, readOnly: false, ensureCreated: false, Logger);
            targetState = probe.IsUpgradeNeeded();
        }
        catch (DbException ex)
        {
            Logger.Error($"Failed to merge into database '{targetFile}': {ex.Message}");

            return;
        }

        if (targetState.CurrentVersion == ProviderDatabaseSchemaVersion.Unknown)
        {
            Logger.Error($"{SchemaStateMessages.UnrecognizedSchema(SchemaStateMessages.TargetLabel, targetFile)}");

            return;
        }

        if (targetState.NeedsUpgrade)
        {
            Logger.Error($"Target database '{targetFile}' is at schema v{targetState.CurrentVersion} but v{ProviderDatabaseSchemaVersion.Current} is required. Run the 'upgrade' command first.");

            return;
        }

        // Destructive ops beyond this point (ExecuteDelete commits immediately; SaveChanges writes) —
        // exceptions propagate so partial-state failures aren't masked as a benign "merge failed".
        using var targetContext = new ProviderDbContext(targetFile, false, Logger);

        // Query the overlap in the database by chunking sourceNames into IN-clause batches,
        // rather than pulling every target ProviderName into memory. Same chunk size as the
        // delete loop below to stay below SQLite's default parameter limit (999).
        var sourceNamesList = sourceNames.ToList();
        var targetMatchingNames = new List<string>();

        for (var offset = 0; offset < sourceNamesList.Count; offset += ProviderSource.MaxInClauseParameters)
        {
            var chunk = sourceNamesList
                .Skip(offset)
                .Take(ProviderSource.MaxInClauseParameters)
                .ToList();

            targetMatchingNames.AddRange(
                targetContext.ProviderDetails
                    .AsNoTracking()
                    .Where(p => chunk.Contains(p.ProviderName))
                    .Select(p => p.ProviderName));
        }

        // Track the source-side provider names whose case-insensitive equivalent exists in target.
        // ProviderName is the primary key in the target DB, so case-sensitive uniqueness identifies
        // a row; the case-insensitive HashSet drives the no-overwrite skip on the source side.
        var providerNamesInTarget = new HashSet<string>(targetMatchingNames, StringComparer.OrdinalIgnoreCase);

        if (targetMatchingNames.Count > 0)
        {
            Logger.Information($"The target database contains {targetMatchingNames.Count} provider row(s) matching {providerNamesInTarget.Count} provider name(s) in the source.");

            if (overwriteProviders)
            {
                Logger.Information($"Removing these providers from the target database...");

                // Chunk the IN-clause to stay below SQLite's parameter limit (default 999). Without
                // chunking, an --overwrite of a large overlap could throw at runtime.
                // ExecuteDelete() issues a SQL DELETE directly, avoiding change-tracker overhead.
                var removed = 0;

                for (var offset = 0; offset < targetMatchingNames.Count; offset += ProviderSource.MaxInClauseParameters)
                {
                    var chunk = targetMatchingNames
                        .Skip(offset)
                        .Take(ProviderSource.MaxInClauseParameters)
                        .ToList();

                    removed += targetContext.ProviderDetails
                        .Where(p => chunk.Contains(p.ProviderName))
                        .ExecuteDelete();
                }

                Logger.Information($"Removal of {removed} provider row(s) completed.");
            }
            else
            {
                Logger.Information($"These providers will not be copied from the source.");
            }
        }

        Logger.Information($"Copying providers from the source...");

        // When not overwriting, pass the overlap as the skip set so providers that already exist
        // in the target are never resolved from the source's metadata path. When overwriting, no
        // skip set is passed so all source providers are loaded and re-inserted.
        var skipForLoad = overwriteProviders ? null : providerNamesInTarget;

        var expectedCopiedNames = skipForLoad is null
            ? sourceNames.ToList()
            : sourceNames.Where(n => !skipForLoad.Contains(n)).ToList();

        Logger.Information($"");
        LogProviderDetailHeader(expectedCopiedNames);

        // Stream details into the DbContext with periodic SaveChanges. The pending batch list is
        // bounded by batchSize so memory stays flat regardless of source size; details are logged
        // AFTER each successful SaveChanges so the printed rows reflect what actually persisted
        // (a SaveChanges failure won't cause the log to overstate progress).
        const int BatchSize = 100;
        var copiedCount = 0;
        var pendingBatch = new List<ProviderDetails>(BatchSize);

        foreach (var provider in ProviderSource.LoadProviders(source, Logger, filter: null, skipProviderNames: skipForLoad))
        {
            targetContext.ProviderDetails.Add(provider);
            pendingBatch.Add(provider);

            if (pendingBatch.Count < BatchSize) { continue; }

            FlushBatch(targetContext, pendingBatch, ref copiedCount);
        }

        if (pendingBatch.Count > 0)
        {
            FlushBatch(targetContext, pendingBatch, ref copiedCount);
        }

        Logger.Information($"");
        Logger.Information($"Copied {copiedCount} provider(s).");
    }

    private void FlushBatch(ProviderDbContext context, List<ProviderDetails> batch, ref int copiedCount)
    {
        context.SaveChanges();

        foreach (var details in batch)
        {
            LogProviderDetails(details);
        }

        copiedCount += batch.Count;
        batch.Clear();
        context.ChangeTracker.Clear();
    }
}
