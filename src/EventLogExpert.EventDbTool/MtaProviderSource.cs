// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Providers;
using EventLogExpert.Eventing.Readers;
using System.Text.RegularExpressions;

namespace EventLogExpert.EventDbTool;

/// <summary>
///     Loads <see cref="ProviderDetails" /> from an exported .evtx log paired with its sibling
///     LocaleMetaData/*.MTA files. Provider names are discovered by reading the .evtx events,
///     and the MTA files are used as the only metadata source (no registry/DLL fallback).
/// </summary>
internal static class MtaProviderSource
{
    /// <summary>
    ///     Reads <paramref name="evtxPath" /> and returns the distinct provider names referenced by its
    ///     event records. Does NOT require sibling LocaleMetaData/*.MTA files.
    /// </summary>
    public static IReadOnlyList<string> DiscoverProviderNames(
        string evtxPath,
        ITraceLogger logger,
        string? filter = null)
    {
        if (!File.Exists(evtxPath))
        {
            logger.Error($"Evtx file not found: {evtxPath}");
            return [];
        }

        var providerNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var reader = new EventLogReader(evtxPath, PathType.FilePath);

            if (!reader.IsValid)
            {
                logger.Error($"Failed to open {evtxPath} for reading. The file may be missing, corrupt, or inaccessible.");
                return [];
            }

            while (reader.TryGetEvents(out var batch))
            {
                if (batch.Length == 0) { break; }

                foreach (var record in batch)
                {
                    if (!string.IsNullOrEmpty(record.ProviderName))
                    {
                        providerNames.Add(record.ProviderName);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.Error($"Failed to read events from {evtxPath}: {ex.Message}");
            return [];
        }

        if (string.IsNullOrEmpty(filter))
        {
            return providerNames.ToList();
        }

        var regex = new Regex(filter, RegexOptions.IgnoreCase);

        return providerNames.Where(n => regex.IsMatch(n)).ToList();
    }

    /// <summary>
    ///     Returns the sibling LocaleMetaData/*.MTA files for <paramref name="evtxPath" />, or an empty
    ///     array if none are present. Logs an error in the empty case to surface the misconfiguration
    ///     without throwing.
    /// </summary>
    public static IReadOnlyList<string> FindMtaFiles(string evtxPath, ITraceLogger logger)
    {
        var logDir = Path.GetDirectoryName(Path.GetFullPath(evtxPath));

        if (logDir is null)
        {
            logger.Error($"Could not determine directory for {evtxPath}.");
            return [];
        }

        var localeDir = Path.Combine(logDir, "LocaleMetaData");

        if (!Directory.Exists(localeDir))
        {
            logger.Error(
                $"No LocaleMetaData folder found next to {evtxPath}. " +
                $"MTA resolution requires the sibling LocaleMetaData folder produced when the log was exported.");

            return [];
        }

        var mtaFiles = Directory.GetFiles(localeDir, "*.MTA");
        Array.Sort(mtaFiles, StringComparer.Ordinal);

        if (mtaFiles.Length == 0)
        {
            logger.Error($"LocaleMetaData folder at {localeDir} contains no MTA files.");
            return [];
        }

        logger.Info($"Using {mtaFiles.Length} locale metadata file(s) from {localeDir}.");

        return mtaFiles;
    }

    /// <summary>
    ///     Discovers provider names from <paramref name="evtxPath" /> and yields a
    ///     <see cref="ProviderDetails" /> for each that can be resolved exclusively from the sibling
    ///     LocaleMetaData/*.MTA files. Providers that cannot be resolved from any MTA file are
    ///     skipped with a warning so callers never persist empty placeholder providers (which would
    ///     defeat the "no local fallback" guarantee when the resulting database is consumed later).
    /// </summary>
    public static IEnumerable<ProviderDetails> LoadProviders(
        string evtxPath,
        ITraceLogger logger,
        string? filter = null) =>
        LoadProvidersCore(evtxPath, logger, string.IsNullOrEmpty(filter) ? null : new Regex(filter, RegexOptions.IgnoreCase), null, null);

    /// <summary>
    ///     Internal overload used by <see cref="ProviderSource" /> so name-based filtering and the de-dup
    ///     <c>seen</c> set are applied BEFORE the expensive MTA resolution per provider.
    /// </summary>
    internal static IEnumerable<ProviderDetails> LoadProviders(
        string evtxPath,
        ITraceLogger logger,
        Regex? regex,
        IReadOnlySet<string>? skipProviderNames,
        HashSet<string>? seen) =>
        LoadProvidersCore(evtxPath, logger, regex, skipProviderNames, seen);

    private static bool IsEmpty(ProviderDetails details) =>
        details.Events.Count == 0 &&
        details.Keywords.Count == 0 &&
        details.Messages.Count == 0 &&
        details.Opcodes.Count == 0 &&
        details.Tasks.Count == 0 &&
        !details.Parameters.Any();

    private static IEnumerable<ProviderDetails> LoadProvidersCore(
        string evtxPath,
        ITraceLogger logger,
        Regex? regex,
        IReadOnlySet<string>? skipProviderNames,
        HashSet<string>? seen)
    {
        var providerNames = DiscoverProviderNames(evtxPath, logger);

        if (providerNames.Count == 0) { yield break; }

        var mtaFiles = FindMtaFiles(evtxPath, logger);

        // Without MTA files, EventMessageProvider would fall back to the local registry/DLLs,
        // producing data attributable to this machine rather than the exported log. Refuse.
        if (mtaFiles.Count == 0) { yield break; }

        foreach (var providerName in providerNames)
        {
            if (regex is not null && !regex.IsMatch(providerName)) { continue; }
            if (skipProviderNames is not null && skipProviderNames.Contains(providerName)) { continue; }
            if (seen is not null && !seen.Add(providerName)) { continue; }

            var details = new EventMessageProvider(providerName, null, mtaFiles, logger).LoadProviderDetails();

            if (IsEmpty(details))
            {
                logger.Warn($"Skipping {providerName}: not found in any MTA file next to {evtxPath}.");
                continue;
            }

            yield return details;
        }
    }
}
