// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventProviderDatabase;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Providers;
using Microsoft.EntityFrameworkCore;
using System.Data.Common;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EventLogExpert.EventDbTool;

/// <summary>
///     Loads <see cref="ProviderDetails" /> from a path that may be a .db file, an exported .evtx file, or a
///     folder. When the path is a folder, all top-level *.db files are processed first (sorted), followed by
///     all top-level *.evtx files (sorted). Subdirectories are not searched.
/// </summary>
internal static class ProviderSource
{
    /// <summary>
    ///     Conservative cap on the number of parameters in a single <c>Where(... Contains)</c> SQL IN
    ///     clause. SQLite's default limit is 999 parameters; we stay well under that so the same code
    ///     works on older SQLite builds too. Larger requests are split into multiple round-trips.
    /// </summary>
    internal const int MaxInClauseParameters = 500;

    private const string DbExtension = ".db";
    private const string EvtxExtension = ".evtx";

    /// <summary>
    ///     Returns the distinct provider names available from <paramref name="path" />, applying an optional
    ///     case-insensitive regex <paramref name="filter" />. Does not load full provider details.
    /// </summary>
    public static IReadOnlyList<string> LoadProviderNames(string path, ITraceLogger logger, string? filter = null) =>
        !RegexHelper.TryCreate(filter, logger, out var regex) ? [] : LoadProviderNames(path, logger, regex);

    /// <inheritdoc cref="LoadProviderNames(string, ITraceLogger, string?)"/>
    public static IReadOnlyList<string> LoadProviderNames(string path, ITraceLogger logger, Regex? regex)
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in EnumerateSourceFiles(path, logger))
        {
            foreach (var name in LoadNamesFromFile(file, logger))
            {
                names.Add(name);
            }
        }

        return regex is null ? names.ToList() : names.Where(n => regex.IsMatch(n)).ToList();
    }

    /// <summary>
    ///     Loads <see cref="ProviderDetails" /> from <paramref name="path" />, applying an optional
    ///     case-insensitive regex <paramref name="filter" /> to provider names. When the same provider name
    ///     appears in multiple source files, the first occurrence wins (.db files are processed before .evtx).
    ///     Provider names contained in <paramref name="skipProviderNames" /> are excluded BEFORE details are
    ///     resolved, so callers using the skip set never pay the cost of loading metadata for excluded providers.
    /// </summary>
    public static IEnumerable<ProviderDetails> LoadProviders(
        string path,
        ITraceLogger logger,
        string? filter = null,
        IReadOnlySet<string>? skipProviderNames = null) =>
        !RegexHelper.TryCreate(filter, logger, out var regex) ? [] :
            LoadProvidersIterator(path, logger, regex, skipProviderNames);

    /// <inheritdoc cref="LoadProviders(string, ITraceLogger, string?, IReadOnlySet{string}?)"/>
    public static IEnumerable<ProviderDetails> LoadProviders(
        string path,
        ITraceLogger logger,
        Regex? regex,
        IReadOnlySet<string>? skipProviderNames = null) =>
        LoadProvidersIterator(path, logger, regex, skipProviderNames);

    /// <summary>Validates that <paramref name="path" /> exists and has a recognized form.</summary>
    public static bool TryValidate(string path, ITraceLogger logger)
    {
        if (Directory.Exists(path))
        {
            // Probe directory accessibility up front so callers fail fast with a clear message
            // rather than silently producing wrong output later when EnumerateSourceFiles falls
            // back to an empty sequence on an UnauthorizedAccessException/IOException.
            try
            {
                Directory.GetFiles(path);
                return true;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                logger.Error($"Cannot read source folder '{path}': {ex.Message}");
                return false;
            }
        }

        if (!File.Exists(path))
        {
            logger.Error($"Source not found: {path}");
            return false;
        }

        var extension = Path.GetExtension(path);

        if (string.Equals(extension, DbExtension, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, EvtxExtension, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        logger.Error($"Unsupported source file extension '{extension}'. Expected .db, .evtx, or a folder containing them.");
        return false;
    }

    /// <summary>
    ///     Returns true when every <c>.db</c> file enumerated from <paramref name="path" /> is at the current
    ///     schema. When any file is at an unrecognized or stale schema, an actionable error is logged for that
    ///     file and the method returns false. Use this before workflows where a schema-rejected source file
    ///     silently producing zero providers would corrupt the result (e.g., diff, where the first source
    ///     defines the baseline of what to skip from the second source). Single-file workflows that skip
    ///     individual unreadable sources can rely on the per-file gate inside <see cref="LoadProviderNames(string, ITraceLogger, string?)" />
    ///     and friends, which logs and yields no rows for the failing file. <c>.evtx</c> files have no schema
    ///     and are ignored here.
    /// </summary>
    public static bool ValidateSourceSchemas(string path, ITraceLogger logger)
    {
        var allOk = true;

        foreach (var file in EnumerateSourceFiles(path, logger))
        {
            if (!string.Equals(Path.GetExtension(file), DbExtension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                using var providerContext = new EventProviderDbContext(file, true, logger);

                if (!IsSourceSchemaCurrent(providerContext, file, logger))
                {
                    allOk = false;
                }
            }
            catch (DbException ex)
            {
                logger.Error($"Cannot open source database '{file}': {ex.Message}");
                allOk = false;
            }
        }

        return allOk;
    }

    /// <summary>
    ///     Expands <paramref name="path" /> into the ordered list of source files: a single .db or .evtx
    ///     when given a file; or all *.db files (sorted) followed by all *.evtx files (sorted) when given a
    ///     folder.
    /// </summary>
    private static IEnumerable<string> EnumerateSourceFiles(string path, ITraceLogger logger)
    {
        if (!Directory.Exists(path))
        {
            yield return path;
            yield break;
        }

        // TryValidate already probed accessibility, but a transient IO/permission change between
        // validation and enumeration is still possible. Catch and log so the tool warns instead
        // of crashing; an empty result here means the caller sees no source files (handled
        // elsewhere with a "no providers" warning).
        string[] allFiles;

        try
        {
            allFiles = Directory.GetFiles(path);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            logger.Error($"Cannot read source folder '{path}': {ex.Message}");
            yield break;
        }

        // Use case-insensitive extension comparison so that .DB / .EVTX (and any other case
        // variants permitted by case-sensitive directories on Windows) are picked up the same
        // way TryValidate accepts them. Files are bucketed (.db first, then .evtx) and sorted
        // OrdinalIgnoreCase within each bucket so first-occurrence-wins ordering is stable.
        var dbFiles = allFiles
            .Where(f => string.Equals(System.IO.Path.GetExtension(f), DbExtension, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Array.Sort(dbFiles, StringComparer.OrdinalIgnoreCase);

        foreach (var f in dbFiles) { yield return f; }

        var evtxFiles = allFiles
            .Where(f => string.Equals(System.IO.Path.GetExtension(f), EvtxExtension, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Array.Sort(evtxFiles, StringComparer.OrdinalIgnoreCase);

        foreach (var f in evtxFiles) { yield return f; }
    }

    private static IEnumerable<ProviderDetails> LoadDetailsFromFile(
        string file,
        ITraceLogger logger,
        Regex? regex,
        IReadOnlySet<string>? skipProviderNames,
        HashSet<string> seen)
    {
        var ext = Path.GetExtension(file);

        if (string.Equals(ext, DbExtension, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var context = new EventProviderDbContext(file, true, logger);

                if (!IsSourceSchemaCurrent(context, file, logger)) { return []; }

                context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

                // Filter by name without mutating `seen` so that a subsequent catch does not
                // permanently mark these names as loaded when they were never successfully read.
                var allNames = context.ProviderDetails.Select(p => p.ProviderName).ToList();
                var namesToLoad = allNames
                    .Where(name =>
                    {
                        if (seen.Contains(name)) { return false; }
                        if (regex is not null && !regex.IsMatch(name)) { return false; }

                        return skipProviderNames is null || !skipProviderNames.Contains(name);
                    })
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (namesToLoad.Count == 0) { return []; }

                // Chunk the IN-clause to stay below SQLite's parameter limit (default 999).
                var loaded = new List<ProviderDetails>(namesToLoad.Count);

                for (var offset = 0; offset < namesToLoad.Count; offset += MaxInClauseParameters)
                {
                    var chunk = namesToLoad
                        .Skip(offset)
                        .Take(MaxInClauseParameters)
                        .ToList();

                    loaded.AddRange(context.ProviderDetails
                        .Where(p => chunk.Contains(p.ProviderName))
                        .OrderBy(p => p.ProviderName));
                }

                // Mark as seen only after a successful load so a catch for a corrupt file does
                // not prevent the same provider names from being loaded from a later source file.
                foreach (var name in namesToLoad) { seen.Add(name); }

                return loaded;
            }
            catch (Exception ex) when (ex is DbException or JsonException or InvalidDataException)
            {
                logger.Warn($"Skipping invalid database file '{file}': {ex.Message}");
                return [];
            }
        }

        if (string.Equals(ext, EvtxExtension, StringComparison.OrdinalIgnoreCase))
        {
            return MtaProviderSource.LoadProviders(file, logger, regex, skipProviderNames, seen);
        }

        logger.Warn($"Skipping unsupported source file: {file}");

        return [];
    }

    private static IEnumerable<string> LoadNamesFromFile(string file, ITraceLogger logger)
    {
        var ext = Path.GetExtension(file);

        if (string.Equals(ext, DbExtension, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var providerContext = new EventProviderDbContext(file, true, logger);

                return !IsSourceSchemaCurrent(providerContext, file, logger) ? [] :
                    providerContext.ProviderDetails.AsNoTracking().Select(p => p.ProviderName).ToList();
            }
            catch (DbException ex)
            {
                logger.Warn($"Skipping invalid database file '{file}': {ex.Message}");
                return [];
            }
        }

        if (string.Equals(ext, EvtxExtension, StringComparison.OrdinalIgnoreCase))
        {
            return MtaProviderSource.DiscoverProviderNames(file, logger);
        }

        logger.Warn($"Skipping unsupported source file: {file}");

        return [];
    }

    /// <summary>
    /// Returns true if <paramref name="context"/> points to a database at the current schema
    /// version. Otherwise logs an explicit error and returns false. Without this gate, EF would
    /// query the source with the V4 model against a V3 schema and either throw a confusing
    /// <c>DbException</c> for the missing column or — in the names-only path — silently discover
    /// names that subsequently fail to materialize as full provider rows. Distinguishes the
    /// "unrecognized schema" case from the "known but stale" case so the error message is actionable.
    /// </summary>
    private static bool IsSourceSchemaCurrent(EventProviderDbContext context, string file, ITraceLogger logger)
    {
        var state = context.IsUpgradeNeeded();

        if (!state.NeedsUpgrade) { return true; }

        if (state.CurrentVersion == ProviderDatabaseSchemaVersion.Unknown)
        {
            logger.Error(
                $"Source database '{file}' has an unrecognized schema. The file may be corrupt or from a newer or incompatible version of EventLogExpert. Delete or replace the file.");
        }
        else
        {
            logger.Error(
                $"Source database '{file}' is at schema v{state.CurrentVersion} but v{ProviderDatabaseSchemaVersion.Current} is required. Run the 'upgrade' command on the source first.");
        }

        return false;
    }

    private static IEnumerable<ProviderDetails> LoadProvidersIterator(
        string path,
        ITraceLogger logger,
        Regex? regex,
        IReadOnlySet<string>? skipProviderNames)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in EnumerateSourceFiles(path, logger))
        {
            foreach (var details in LoadDetailsFromFile(file, logger, regex, skipProviderNames, seen))
            {
                yield return details;
            }
        }
    }
}
