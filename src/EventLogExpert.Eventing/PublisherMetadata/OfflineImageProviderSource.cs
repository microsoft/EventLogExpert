// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.PublisherMetadata.Offline;
using EventLogExpert.Eventing.PublisherMetadata.Offline.Containment;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Resolution;
using System.Text.RegularExpressions;

namespace EventLogExpert.Eventing.PublisherMetadata;

/// <summary>
///     Loads <see cref="ProviderDetails" /> for every provider in a mounted or extracted foreign Windows image, fully
///     offline. Modern (manifest) providers come from the image's WINEVT catalog and pure-legacy providers from its
///     <c>SYSTEM\Services\EventLog</c> registrations; each is built with file paths re-rooted onto the image and stamped
///     with the IMAGE's OS provenance - the host registry and host files are never read. Mirrors the live
///     <see cref="MtaProviderSource" /> shape (yields, skips empty, de-dups by name). The yielded
///     <see cref="ProviderDetails" /> carry LAZY message/parameter sources that reopen the re-rooted DLL files on demand,
///     so the IMAGE must remain mounted/readable until the caller materializes or persists them - the same lifetime
///     contract as the live and MTA sources.
/// </summary>
public static class OfflineImageProviderSource
{
    /// <summary>
    ///     Enumerates every provider in the image at <paramref name="imageRootPath" /> (either the image root or its
    ///     <c>Windows</c> directory), optionally filtered by <paramref name="regex" /> /
    ///     <paramref name="excludeProviderNames" /> on the provider name. Yields nothing (after a logged error) when the path
    ///     is not a readable Windows image or its hives cannot be safely loaded; it never throws for a bad or hostile image.
    /// </summary>
    public static IEnumerable<ProviderDetails> LoadProviders(
        string imageRootPath,
        ITraceLogger logger,
        Regex? regex = null,
        IReadOnlySet<string>? excludeProviderNames = null)
    {
        // The `using` spans the whole enumeration, so the hives are loaded while enumerating and unloaded when
        // iteration completes (or the caller breaks/throws); nothing is loaded if the caller never enumerates.
        using IOfflineImageProviderExtractor? extractor = TryCreateExtractor(imageRootPath, logger);

        if (extractor is null) { yield break; }

        foreach (ProviderDetails details in Enumerate(extractor, regex, excludeProviderNames))
        {
            yield return details;
        }
    }

    /// <summary>
    ///     The enumeration / de-dup / provenance-stamping orchestration, separated from hive loading so it can be
    ///     unit-tested with a fake <see cref="IOfflineImageProviderExtractor" />. A modern provider wins over a pure-legacy
    ///     provider of the same name (the modern build already populated the legacy tables); empty providers are skipped, and
    ///     a name is marked seen only after a non-empty provider is yielded.
    /// </summary>
    internal static IEnumerable<ProviderDetails> Enumerate(
        IOfflineImageProviderExtractor extractor,
        Regex? regex,
        IReadOnlySet<string>? excludeProviderNames)
    {
        SourceOsProvenance provenance = extractor.ReadImageProvenance();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (OfflinePublisherRegistration registration in extractor.ReadModernRegistrations())
        {
            if (IsFilteredOut(registration.ProviderName, regex, excludeProviderNames)) { continue; }

            if (extractor.TryBuildModernProvider(registration) is not { IsEmpty: false } details) { continue; }

            if (!seen.Add(registration.ProviderName)) { continue; }

            Stamp(details, provenance);

            yield return details;
        }

        foreach (string providerName in extractor.EnumerateLegacyProviderNames())
        {
            if (seen.Contains(providerName) || IsFilteredOut(providerName, regex, excludeProviderNames)) { continue; }

            if (extractor.TryBuildLegacyProvider(providerName) is not { IsEmpty: false } details) { continue; }
            
            if (!seen.Add(providerName)) { continue; }

            Stamp(details, provenance);

            yield return details;
        }
    }

    private static bool IsFilteredOut(string providerName, Regex? regex, IReadOnlySet<string>? excludeProviderNames) =>
        (regex is not null && !regex.IsMatch(providerName)) ||
        (excludeProviderNames is not null && excludeProviderNames.Contains(providerName));

    private static void Stamp(ProviderDetails details, SourceOsProvenance provenance)
    {
        details.SourceOsBuild = provenance.Build;
        details.SourceOsRevision = provenance.Revision;
        details.SourceOsEdition = provenance.Edition;
        details.SourceOsDisplayVersion = provenance.DisplayVersion;
    }

    /// <summary>
    ///     Resolves and loads the image's hives, logging and returning <see langword="null" /> for every "not a usable
    ///     image" outcome - an unreadable path, hives that cannot be loaded, or hive paths that escape the image root (a
    ///     malformed or hostile image, surfaced by <see cref="OfflineImageProviderExtractor.TryCreate" /> as an
    ///     <see cref="OfflineRootGuardViolationException" />). Kept out of the iterator so it can use a try/catch, which lets
    ///     the public facade translate that fail-closed signal into its documented "logged error + yield nothing" contract
    ///     instead of throwing from deep inside a lazy enumeration.
    /// </summary>
    private static IOfflineImageProviderExtractor? TryCreateExtractor(string imageRootPath, ITraceLogger logger)
    {
        if (OfflineImageRoot.TryCreate(imageRootPath, logger) is not { } imageRoot)
        {
            logger.Error($"'{imageRootPath}' is not a readable Windows image (no SOFTWARE/SYSTEM hive found).");

            return null;
        }

        try
        {
            IOfflineImageProviderExtractor? extractor = OfflineImageProviderExtractor.TryCreate(imageRoot, logger);

            if (extractor is null)
            {
                logger.Error($"Could not load the SOFTWARE/SYSTEM hives from '{imageRootPath}'.");
            }

            return extractor;
        }
        catch (OfflineRootGuardViolationException ex)
        {
            logger.Error(
                $"The image at '{imageRootPath}' has hive paths that escape the image root; refusing to read it. {ex.Message}");

            return null;
        }
    }
}
