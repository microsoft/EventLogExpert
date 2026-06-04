// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Resolution;
using System.Text.RegularExpressions;

namespace EventLogExpert.Eventing.PublisherMetadata;

/// <summary>
///     Loads <see cref="ProviderDetails" /> from an exported .evtx log paired with its sibling LocaleMetaData/*.MTA
///     files. Provider names are discovered by reading the .evtx events, and the MTA files are used as the only metadata
///     source (no registry/DLL fallback).
/// </summary>
public static class MtaProviderSource
{
    /// <summary>
    ///     Reads <paramref name="evtxPath" /> and returns the distinct provider names referenced by its event records.
    ///     Does NOT require sibling LocaleMetaData/*.MTA files.
    /// </summary>
    public static IReadOnlyList<string> DiscoverProviderNames(
        string evtxPath,
        ITraceLogger logger,
        Regex? regex = null) =>
        DiscoverProviderNamesCore(evtxPath, logger, regex);

    /// <summary>
    ///     Returns the sibling LocaleMetaData/*.MTA files for <paramref name="evtxPath" />, or an empty array if none are
    ///     present. Logs an error in the empty case to surface the misconfiguration without throwing.
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

        string[] mtaFiles;

        try
        {
            mtaFiles = Directory.GetFiles(localeDir, "*.MTA");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            logger.Error($"Cannot read LocaleMetaData folder '{localeDir}': {ex.Message}");

            return [];
        }

        Array.Sort(mtaFiles, StringComparer.Ordinal);

        if (mtaFiles.Length == 0)
        {
            logger.Error($"LocaleMetaData folder at {localeDir} contains no MTA files.");

            return [];
        }

        logger.Information($"Using {mtaFiles.Length} locale metadata file(s) from {localeDir}.");

        return mtaFiles;
    }

    public static IEnumerable<ProviderDetails> LoadProviders(
        string evtxPath,
        ITraceLogger logger,
        Regex? regex = null,
        IReadOnlySet<string>? skipProviderNames = null,
        HashSet<string>? seen = null,
        IReadOnlyList<string>? preDiscoveredProviderNames = null) =>
        LoadProvidersCore(evtxPath, logger, regex, skipProviderNames, seen, preDiscoveredProviderNames);

    private static IReadOnlyList<string> DiscoverProviderNamesCore(
        string evtxPath,
        ITraceLogger logger,
        Regex? regex)
    {
        if (!File.Exists(evtxPath))
        {
            logger.Error($"Evtx file not found: {evtxPath}");

            return [];
        }

        var providerNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var reader = new EventLogReader(evtxPath, LogPathType.File);

            if (!reader.IsValid)
            {
                logger.Error($"Failed to open {evtxPath} for reading. The file may be missing, corrupt, or inaccessible.");

                return [];
            }

            // TryGetEvents returns false both for normal end-of-results (ERROR_NO_MORE_ITEMS) and
            // for read errors (corruption, access denied, etc.). Check LastErrorCode to surface
            // non-terminal failures so users can distinguish "0 events" from "could not read the log".
            while (reader.TryGetEvents(out var batch))
            {
                foreach (var record in batch)
                {
                    if (!string.IsNullOrEmpty(record.ProviderName))
                    {
                        providerNames.Add(record.ProviderName);
                    }
                }
            }

            if (reader.LastErrorCode is not null)
            {
                logger.Warning(
                    $"Reading {evtxPath} may be incomplete. " +
                    $"EvtNext failed with Win32 error code {reader.LastErrorCode}.");
            }
        }
        catch (Exception ex)
        {
            logger.Error($"Failed to read events from {evtxPath}: {ex.Message}");

            return [];
        }

        return regex is null ? providerNames.ToList() : providerNames.Where(n => regex.IsMatch(n)).ToList();
    }

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
        HashSet<string>? seen,
        IReadOnlyList<string>? preDiscoveredProviderNames = null)
    {
        // Honor the pre-discovered hint when supplied; otherwise re-scan the evtx file. The hint must already
        // reflect any regex filtering the caller intended — this method does NOT re-apply `regex` to the hint.
        var providerNames = preDiscoveredProviderNames is { Count: > 0 }
            ? preDiscoveredProviderNames
            : DiscoverProviderNamesCore(evtxPath, logger, regex);

        if (providerNames.Count == 0) { yield break; }

        var mtaFiles = FindMtaFiles(evtxPath, logger);

        // Without MTA files, EventMessageProvider would fall back to the local registry/DLLs,
        // producing data attributable to this machine rather than the exported log. Refuse.
        if (mtaFiles.Count == 0) { yield break; }

        foreach (var providerName in providerNames)
        {
            if (skipProviderNames is not null && skipProviderNames.Contains(providerName)) { continue; }

            if (seen is not null && seen.Contains(providerName)) { continue; }

            var details = new EventMessageProvider(providerName, mtaFiles, logger).LoadProviderDetails();

            if (IsEmpty(details))
            {
                logger.Warning($"Skipping {providerName}: not found in any MTA file next to {evtxPath}.");

                continue;
            }

            // Mark as seen only after confirming the provider has data, so that an empty/missing
            // provider from one .evtx does not block loading it from a later source file.
            seen?.Add(providerName);

            yield return details;
        }
    }
}
