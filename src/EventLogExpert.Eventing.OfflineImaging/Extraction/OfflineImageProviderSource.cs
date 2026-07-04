// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.OfflineImaging.Containment;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Resolution;
using System.Text.RegularExpressions;

namespace EventLogExpert.Eventing.OfflineImaging.Extraction;

public static class OfflineImageProviderSource
{
    public static IEnumerable<ProviderDetails> LoadProviders(
        string imageRootPath,
        ITraceLogger logger,
        Regex? regex = null,
        IReadOnlySet<string>? excludeProviderNames = null)
    {
        // Keep hives loaded for the lazy enumeration and unload them when iteration ends.
        using IOfflineImageProviderExtractor? extractor = TryCreateExtractor(imageRootPath, logger);

        if (extractor is null) { yield break; }

        foreach (ProviderDetails details in Enumerate(extractor, regex, excludeProviderNames))
        {
            yield return details;
        }
    }

    // Modern registrations win; mark names seen only after yielding a non-empty provider.
    internal static IEnumerable<ProviderDetails> Enumerate(
        IOfflineImageProviderExtractor extractor,
        Regex? regex,
        IReadOnlySet<string>? excludeProviderNames)
    {
        SourceOsProvenance provenance = extractor.ReadImageProvenance();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (OfflinePublisherRegistration registration in extractor.ReadModernRegistrations())
        {
            if (seen.Contains(registration.ProviderName) || IsFilteredOut(registration.ProviderName, regex, excludeProviderNames)) { continue; }

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

    // Keep this out of the iterator so image-root failures log and yield nothing before lazy enumeration starts.
    private static IOfflineImageProviderExtractor? TryCreateExtractor(string imageRootPath, ITraceLogger logger)
    {
        ITraceLogger providersLogger = logger.ForCategory(LogCategories.OfflineProviders);

        if (OfflineImageRoot.TryCreate(imageRootPath, providersLogger) is not { } imageRoot)
        {
            providersLogger.Error($"'{imageRootPath}' is not a readable Windows image (no SOFTWARE/SYSTEM hive found).");

            return null;
        }

        try
        {
            IOfflineImageProviderExtractor? extractor = OfflineImageProviderExtractor.TryCreate(imageRoot, providersLogger);

            if (extractor is null)
            {
                providersLogger.Debug($"Could not load the SOFTWARE/SYSTEM hives from '{imageRootPath}'.");
            }

            return extractor;
        }
        catch (OfflineRootGuardViolationException ex)
        {
            providersLogger.Error(
                $"The image at '{imageRootPath}' has hive paths that escape the image root; refusing to read it. {ex.Message}");

            return null;
        }
    }
}
