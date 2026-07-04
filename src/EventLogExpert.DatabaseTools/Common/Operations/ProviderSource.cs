// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.ProviderMetadata;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Database.Context;
using EventLogExpert.Provider.Resolution;
using EventLogExpert.Provider.Schema;
using Microsoft.EntityFrameworkCore;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EventLogExpert.DatabaseTools.Common.Operations;

/// <summary>
///     Loads <see cref="ProviderDetails" /> from a path that may be a .db file, an exported .evtx file, or a folder.
///     When the path is a folder, all top-level *.db files are processed first (sorted), followed by all top-level *.evtx
///     files (sorted). Subdirectories are not searched.
/// </summary>
internal static class ProviderSource
{
    /// <summary>
    ///     Conservative cap on the number of parameters in a single <c>Where(... Contains)</c> SQL IN clause. SQLite's
    ///     default limit is 999 parameters; we stay well under that so the same code works on older SQLite builds too. Larger
    ///     requests are split into multiple round-trips.
    /// </summary>
    internal const int MaxInClauseParameters = 500;

    private const string DbExtension = ".db";
    private const string EvtxExtension = ".evtx";

    /// <summary>
    ///     Returns the distinct provider identities (name + content <see cref="ProviderDetails.VersionKey" />) available
    ///     from <paramref name="path" />, applying an optional <paramref name="regex" /> to filter by name. Live providers
    ///     discovered from an .evtx source contribute <c>(name, "")</c> because event records expose only the name. Does not
    ///     load full provider details.
    /// </summary>
    public static async Task<IReadOnlyList<ProviderIdentity>> LoadProviderIdentitiesAsync(
        string path,
        ITraceLogger logger,
        Regex? regex = null,
        CancellationToken cancellationToken = default)
    {
        var identities = new HashSet<ProviderIdentity>();

        foreach (var file in EnumerateSourceFiles(path, logger))
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var identity in await LoadIdentitiesFromFileAsync(file, logger, cancellationToken))
            {
                if (regex is null || regex.IsMatch(identity.ProviderName)) { identities.Add(identity); }
            }
        }

        return identities
            .OrderBy(identity => identity.ProviderName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(identity => identity.ProviderName, StringComparer.Ordinal)
            .ThenBy(identity => identity.VersionKey, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    ///     Returns the distinct provider names available from <paramref name="path" />, applying an optional
    ///     <paramref name="regex" /> to filter names. Case sensitivity follows the caller's <see cref="RegexOptions" />. Does
    ///     not load full provider details.
    /// </summary>
    public static async Task<IReadOnlyList<string>> LoadProviderNamesAsync(
        string path,
        ITraceLogger logger,
        Regex? regex = null,
        CancellationToken cancellationToken = default)
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in EnumerateSourceFiles(path, logger))
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var name in await LoadNamesFromFileAsync(file, logger, cancellationToken))
            {
                names.Add(name);
            }
        }

        return regex is null ? names.ToList() : names.Where(n => regex.IsMatch(n)).ToList();
    }

    /// <summary>
    ///     Streams full <see cref="ProviderDetails" /> from <paramref name="path" />, skipping providers two ways:
    ///     <paramref name="excludeProviderNames" /> drops EVERY version of a named provider (a user / name-level exclude),
    ///     while <paramref name="skipIdentities" /> drops only specific <c>(name, version)</c> identities (e.g. versions
    ///     already present in a merge/diff target). A provider is skipped if either set matches.
    /// </summary>
    public static IAsyncEnumerable<ProviderDetails> LoadProvidersAsync(
        string path,
        ITraceLogger logger,
        Regex? regex = null,
        IReadOnlySet<string>? excludeProviderNames = null,
        IReadOnlySet<ProviderIdentity>? skipIdentities = null,
        IReadOnlyList<string>? preDiscoveredProviderNames = null,
        CancellationToken cancellationToken = default) =>
        LoadProvidersIteratorAsync(path, logger, regex, excludeProviderNames, skipIdentities, preDiscoveredProviderNames, cancellationToken);

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

    public static async Task<bool> ValidateSourceSchemasAsync(string path, ITraceLogger logger, CancellationToken cancellationToken = default)
    {
        var allOk = true;

        foreach (var file in EnumerateSourceFiles(path, logger))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.Equals(Path.GetExtension(file), DbExtension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                await using var providerContext = new ProviderDbContext(file, true, false, logger);

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
    ///     Expands <paramref name="path" /> into the ordered list of source files: a single .db or .evtx when given a
    ///     file; or all *.db files (sorted) followed by all *.evtx files (sorted) when given a folder.
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
            .Where(f => string.Equals(Path.GetExtension(f), DbExtension, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Array.Sort(dbFiles, StringComparer.OrdinalIgnoreCase);

        foreach (var f in dbFiles) { yield return f; }

        var evtxFiles = allFiles
            .Where(f => string.Equals(Path.GetExtension(f), EvtxExtension, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Array.Sort(evtxFiles, StringComparer.OrdinalIgnoreCase);

        foreach (var f in evtxFiles) { yield return f; }
    }

    private static bool IsSourceSchemaCurrent(ProviderDbContext context, string file, ITraceLogger logger)
    {
        var state = context.IsUpgradeNeeded();

        if (!state.NeedsUpgrade) { return true; }

        if (state.CurrentVersion == DatabaseSchemaVersion.Unknown)
        {
            logger.Error(
                $"{SchemaStateMessages.UnrecognizedSchema(SchemaStateMessages.SourceLabel, file)}");
        }
        else
        {
            logger.Error(
                $"Source database '{file}' is at schema v{state.CurrentVersion} but v{DatabaseSchemaVersion.Current} is required. Run the 'upgrade' command on the source first.");
        }

        return false;
    }

    private static async Task<IReadOnlyList<ProviderDetails>> LoadDbDetailsAsync(
        string file,
        ITraceLogger logger,
        Regex? regex,
        IReadOnlySet<string>? excludeProviderNames,
        IReadOnlySet<ProviderIdentity>? skipIdentities,
        HashSet<ProviderIdentity> seen,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var context = new ProviderDbContext(file, true, false, logger);

            if (!IsSourceSchemaCurrent(context, file, logger)) { return []; }

            context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

            // Project (name, version) identities and filter without mutating `seen`, so a subsequent catch does not
            // permanently mark these identities as loaded when they were never successfully read. Dedup is
            // version-aware: an identity already loaded from an earlier source file is skipped, but a different
            // version of the same provider name is not.
            var allIdentities = await context.ProviderDetails
                .Select(p => new { p.ProviderName, p.VersionKey })
                .ToListAsync(cancellationToken);

            var identitiesToLoad = allIdentities
                .Select(p => new ProviderIdentity(p.ProviderName, p.VersionKey))
                .Where(identity =>
                {
                    if (seen.Contains(identity)) { return false; }

                    if (regex is not null && !regex.IsMatch(identity.ProviderName)) { return false; }

                    if (excludeProviderNames is not null && excludeProviderNames.Contains(identity.ProviderName)) { return false; }

                    return skipIdentities is null || !skipIdentities.Contains(identity);
                })
                .ToList();

            if (identitiesToLoad.Count == 0) { return []; }

            var wantedIdentities = identitiesToLoad.ToHashSet();

            // Reload full rows by NAME (SQLite cannot translate a composite-tuple IN clause), chunking to stay below
            // SQLite's parameter limit (default 999). Derive the names from the identity LIST (not the HashSet) and
            // de-duplicate them ORDINALLY: ProviderIdentity equality and SQLite's NOCASE collation differ on non-ASCII
            // case (NOCASE folds only ASCII, OrdinalIgnoreCase folds all of Unicode), so two names differing solely by
            // non-ASCII case are distinct rows that must both reach the IN clause - collapsing them via the HashSet or
            // an OrdinalIgnoreCase Distinct would drop one. The reload by name can pull versions we did not ask for, so
            // post-filter the materialized rows back down to the wanted identities. Today VersionKey is always empty,
            // making the post-filter a no-op; it becomes load-bearing once content hashing lets versions coexist.
            var namesToLoad = identitiesToLoad
                .Select(identity => identity.ProviderName)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var loaded = new List<ProviderDetails>(identitiesToLoad.Count);

            for (var offset = 0; offset < namesToLoad.Count; offset += MaxInClauseParameters)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var chunk = namesToLoad
                    .Skip(offset)
                    .Take(MaxInClauseParameters)
                    .ToList();

                var rows = await context.ProviderDetails
                    .Where(p => chunk.Contains(p.ProviderName))
                    .OrderBy(p => p.ProviderName)
                    .ThenBy(p => p.VersionKey)
                    .ToListAsync(cancellationToken);

                loaded.AddRange(rows.Where(row => wantedIdentities.Contains(ProviderIdentity.Of(row))));
            }

            // Mark as seen only after a successful load so a catch for a corrupt file does
            // not prevent the same identities from being loaded from a later source file.
            foreach (var identity in identitiesToLoad) { seen.Add(identity); }

            return loaded;
        }
        catch (Exception ex) when (ex is DbException or JsonException or InvalidDataException)
        {
            logger.Warning($"Skipping invalid database file '{file}': {ex.Message}");

            return [];
        }
    }

    private static async Task<IReadOnlyList<ProviderIdentity>> LoadIdentitiesFromFileAsync(
        string file,
        ITraceLogger logger,
        CancellationToken cancellationToken)
    {
        var ext = Path.GetExtension(file);

        if (string.Equals(ext, DbExtension, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await using var providerContext = new ProviderDbContext(file, true, false, logger);

                if (!IsSourceSchemaCurrent(providerContext, file, logger)) { return []; }

                var pairs = await providerContext.ProviderDetails
                    .AsNoTracking()
                    .Select(p => new { p.ProviderName, p.VersionKey })
                    .ToListAsync(cancellationToken);

                return pairs.Select(p => new ProviderIdentity(p.ProviderName, p.VersionKey)).ToList();
            }
            catch (DbException ex)
            {
                logger.Warning($"Skipping invalid database file '{file}': {ex.Message}");

                return [];
            }
        }

        if (string.Equals(ext, EvtxExtension, StringComparison.OrdinalIgnoreCase))
        {
            // Live providers from an exported log expose only a name (no content version), so their identity is (name, empty).
            return MtaProviderSource.DiscoverProviderNames(file, logger)
                .Select(name => new ProviderIdentity(name, string.Empty))
                .ToList();
        }

        logger.Warning($"Skipping unsupported source file: {file}");

        return [];
    }

    private static async Task<IReadOnlyList<string>> LoadNamesFromFileAsync(string file, ITraceLogger logger, CancellationToken cancellationToken)
    {
        var ext = Path.GetExtension(file);

        if (string.Equals(ext, DbExtension, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await using var providerContext = new ProviderDbContext(file, true, false, logger);

                return !IsSourceSchemaCurrent(providerContext, file, logger) ? [] :
                    await providerContext.ProviderDetails.AsNoTracking().Select(p => p.ProviderName).ToListAsync(cancellationToken);
            }
            catch (DbException ex)
            {
                logger.Warning($"Skipping invalid database file '{file}': {ex.Message}");

                return [];
            }
        }

        if (string.Equals(ext, EvtxExtension, StringComparison.OrdinalIgnoreCase))
        {
            return MtaProviderSource.DiscoverProviderNames(file, logger);
        }

        logger.Warning($"Skipping unsupported source file: {file}");

        return [];
    }

    private static async IAsyncEnumerable<ProviderDetails> LoadProvidersIteratorAsync(
        string path,
        ITraceLogger logger,
        Regex? regex,
        IReadOnlySet<string>? excludeProviderNames,
        IReadOnlySet<ProviderIdentity>? skipIdentities,
        IReadOnlyList<string>? preDiscoveredProviderNames,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var seen = new HashSet<ProviderIdentity>();
        var files = EnumerateSourceFiles(path, logger).ToList();

        // preDiscoveredProviderNames optimization only applies for single-file .evtx sources. For folders and .db
        // sources, name attribution is per-file or not the bottleneck, so we ignore the hint and fall back to per-file
        // discovery. This keeps the optimization scope-local and safe.
        var canUsePreDiscovered = preDiscoveredProviderNames is not null
            && files.Count == 1
            && string.Equals(Path.GetExtension(files[0]), EvtxExtension, StringComparison.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ext = Path.GetExtension(file);

            if (string.Equals(ext, DbExtension, StringComparison.OrdinalIgnoreCase))
            {
                // Materialize the full per-file result before yielding so a mid-read DbException yields nothing for
                // this file and does not corrupt the cross-file `seen` set. C# also forbids `yield` inside try/catch.
                var loaded = await LoadDbDetailsAsync(file, logger, regex, excludeProviderNames, skipIdentities, seen, cancellationToken);

                foreach (var details in loaded) { yield return details; }
            }
            else if (string.Equals(ext, EvtxExtension, StringComparison.OrdinalIgnoreCase))
            {
                var hint = canUsePreDiscovered ? preDiscoveredProviderNames : null;

                foreach (var details in MtaProviderSource.LoadProviders(file, logger, regex, excludeProviderNames, skipIdentities, seen, hint))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    yield return details;
                }
            }
            else
            {
                logger.Warning($"Skipping unsupported source file: {file}");
            }
        }
    }
}
