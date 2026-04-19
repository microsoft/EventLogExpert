// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventProviderDatabase;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Providers;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace EventLogExpert.EventDbTool;

/// <summary>
///     Loads <see cref="ProviderDetails" /> from a path that may be a .db file, an exported .evtx file, or a
///     folder. When the path is a folder, all top-level *.db files are processed first (sorted), followed by
///     all top-level *.evtx files (sorted). Subdirectories are not searched.
/// </summary>
internal static class ProviderSource
{
    private const string DbExtension = ".db";
    private const string EvtxExtension = ".evtx";

    /// <summary>
    ///     Returns the distinct provider names available from <paramref name="path" />, applying an optional
    ///     case-insensitive regex <paramref name="filter" />. Does not load full provider details.
    /// </summary>
    public static IReadOnlyList<string> LoadProviderNames(string path, ITraceLogger logger, string? filter = null)
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in EnumerateSourceFiles(path))
        {
            foreach (var name in LoadNamesFromFile(file, logger))
            {
                names.Add(name);
            }
        }

        return ApplyFilter(names, filter).ToList();
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
        IReadOnlySet<string>? skipProviderNames = null)
    {
        Regex? regex = string.IsNullOrEmpty(filter) ? null : new Regex(filter, RegexOptions.IgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in EnumerateSourceFiles(path))
        {
            foreach (var details in LoadDetailsFromFile(file, logger, regex, skipProviderNames, seen))
            {
                yield return details;
            }
        }
    }

    /// <summary>Validates that <paramref name="path" /> exists and has a recognized form.</summary>
    public static bool TryValidate(string path, ITraceLogger logger)
    {
        if (Directory.Exists(path)) { return true; }

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

    internal static bool ShouldInclude(
        string providerName,
        Regex? regex,
        IReadOnlySet<string>? skipProviderNames,
        HashSet<string> seen)
    {
        if (regex is not null && !regex.IsMatch(providerName)) { return false; }
        if (skipProviderNames is not null && skipProviderNames.Contains(providerName)) { return false; }

        return seen.Add(providerName);
    }

    private static IEnumerable<string> ApplyFilter(IEnumerable<string> names, string? filter)
    {
        if (string.IsNullOrEmpty(filter)) { return names; }

        var regex = new Regex(filter, RegexOptions.IgnoreCase);

        return names.Where(n => regex.IsMatch(n));
    }

    /// <summary>
    ///     Expands <paramref name="path" /> into the ordered list of source files: a single .db or .evtx
    ///     when given a file; or all *.db files (sorted) followed by all *.evtx files (sorted) when given a
    ///     folder.
    /// </summary>
    private static IEnumerable<string> EnumerateSourceFiles(string path)
    {
        if (Directory.Exists(path))
        {
            var dbFiles = Directory.GetFiles(path, "*" + DbExtension);
            Array.Sort(dbFiles, StringComparer.OrdinalIgnoreCase);

            foreach (var f in dbFiles) { yield return f; }

            var evtxFiles = Directory.GetFiles(path, "*" + EvtxExtension);
            Array.Sort(evtxFiles, StringComparer.OrdinalIgnoreCase);

            foreach (var f in evtxFiles) { yield return f; }

            yield break;
        }

        yield return path;
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
            using var ctx = new EventProviderDbContext(file, true, logger);
            ctx.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

            // Filter by name first so we never materialize the (potentially large) compressed JSON
            // payload for providers we are about to discard.
            var allNames = ctx.ProviderDetails.Select(p => p.ProviderName).ToList();
            var namesToLoad = allNames
                .Where(n => ShouldInclude(n, regex, skipProviderNames, seen))
                .ToList();

            if (namesToLoad.Count == 0) { return []; }

            return ctx.ProviderDetails
                .Where(p => namesToLoad.Contains(p.ProviderName))
                .ToList();
        }

        if (string.Equals(ext, EvtxExtension, StringComparison.OrdinalIgnoreCase))
        {
            return MtaProviderSource.LoadProviders(file, logger, regex, skipProviderNames, seen).ToList();
        }

        logger.Warn($"Skipping unsupported source file: {file}");

        return [];
    }

    private static IEnumerable<string> LoadNamesFromFile(string file, ITraceLogger logger)
    {
        var ext = Path.GetExtension(file);

        if (string.Equals(ext, DbExtension, StringComparison.OrdinalIgnoreCase))
        {
            using var providerContext = new EventProviderDbContext(file, true, logger);

            return providerContext.ProviderDetails.AsNoTracking().Select(p => p.ProviderName).ToList();
        }

        if (string.Equals(ext, EvtxExtension, StringComparison.OrdinalIgnoreCase))
        {
            return MtaProviderSource.DiscoverProviderNames(file, logger);
        }

        logger.Warn($"Skipping unsupported source file: {file}");

        return [];
    }
}
