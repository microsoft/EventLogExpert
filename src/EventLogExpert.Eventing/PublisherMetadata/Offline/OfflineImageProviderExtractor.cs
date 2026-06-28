// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.PublisherMetadata.Offline.Containment;
using EventLogExpert.Eventing.PublisherMetadata.Wevt;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Resolution;

namespace EventLogExpert.Eventing.PublisherMetadata.Offline;

/// <summary>
///     Extracts provider metadata from a mounted or extracted foreign Windows image entirely offline: it loads the
///     image's <c>SOFTWARE</c> and <c>SYSTEM</c> hives, reads the modern publisher catalog and the legacy
///     <c>Services\EventLog</c> registrations, and builds <see cref="ProviderDetails" /> with every file path re-rooted
///     onto the image and guard-checked - never reading host registry or host files. The modern reader is given the
///     image's own <see cref="OfflineLegacyMessageFileResolver" />, so even a manifest provider's legacy table population
///     stays host-free. It exposes the publisher catalog together with per-provider builders for the modern and legacy
///     paths.
/// </summary>
internal sealed class OfflineImageProviderExtractor : IOfflineImageProviderExtractor
{
    private readonly OfflinePublisherCatalog _catalog;
    private readonly OfflineLegacyProviderBuilder _legacyBuilder;
    private readonly OfflineLegacyMessageFileResolver _legacyResolver;
    private readonly ITraceLogger? _logger;
    private readonly OfflineRegistryHive _softwareHive;
    private readonly OfflineRegistryHive _systemHive;

    private OfflineImageProviderExtractor(
        OfflineRegistryHive softwareHive,
        OfflineRegistryHive systemHive,
        OfflinePublisherCatalog catalog,
        OfflineLegacyMessageFileResolver legacyResolver,
        OfflineLegacyProviderBuilder legacyBuilder,
        ITraceLogger? logger)
    {
        _softwareHive = softwareHive;
        _systemHive = systemHive;
        _catalog = catalog;
        _legacyResolver = legacyResolver;
        _legacyBuilder = legacyBuilder;
        _logger = logger;
    }

    /// <summary>
    ///     Loads the image's hives and wires the offline readers, or returns <see langword="null" /> when either hive
    ///     cannot be loaded (logging the specific reason - not a hive, recovery failed, or recovery needs administrator). The
    ///     caller owns the returned extractor and must dispose it (which unloads both hives). Throws
    ///     <see cref="OfflineRootGuardViolationException" /> if the image's hive paths escape the image root (a malformed
    ///     image whose <c>config</c> directory is a junction out of the image).
    /// </summary>
    public static OfflineImageProviderExtractor? TryCreate(OfflineImageRoot imageRoot, ITraceLogger? logger)
    {
        // Jail-check the hive paths BEFORE loading them: if Windows\System32\config is a junction that leaves the image,
        // staging and loading the hive would read out-of-image (possibly host) registry data. Fail closed.
        var guard = new OfflineRootGuard(imageRoot, logger);
        guard.Assert(imageRoot.SoftwareHivePath, "SOFTWARE hive");
        guard.Assert(imageRoot.SystemHivePath, "SYSTEM hive");

        OfflineRegistryHive? softwareHive = LoadHive(imageRoot.SoftwareHivePath, "SOFTWARE", logger);

        if (softwareHive is null) { return null; }

        OfflineRegistryHive? systemHive = LoadHive(imageRoot.SystemHivePath, "SYSTEM", logger);

        if (systemHive is null)
        {
            softwareHive.Dispose();

            return null;
        }

        var pathResolver = new OfflineImagePathResolver(new OfflineImagePathMapper(imageRoot, logger), guard);
        var catalog = new OfflinePublisherCatalog(pathResolver, logger);
        var legacyResolver = new OfflineLegacyMessageFileResolver(systemHive.Root, pathResolver, logger);
        var legacyBuilder = new OfflineLegacyProviderBuilder(legacyResolver, logger);

        return new OfflineImageProviderExtractor(softwareHive, systemHive, catalog, legacyResolver, legacyBuilder, logger);
    }

    public void Dispose()
    {
        _systemHive.Dispose();
        _softwareHive.Dispose();
    }

    /// <summary>Distinct legacy provider names registered under the image's <c>SYSTEM</c> hive.</summary>
    public IReadOnlyList<string> EnumerateLegacyProviderNames() => _legacyResolver.EnumerateProviderNames();

    /// <summary>Reads the image's OS provenance from its <c>SOFTWARE</c> hive (never the host registry).</summary>
    public SourceOsProvenance ReadImageProvenance() => SourceOsProvenance.ReadFromSoftwareHive(_softwareHive.Root, _logger);

    /// <summary>The modern (manifest) publisher registrations declared in the image's <c>SOFTWARE</c> hive.</summary>
    public IReadOnlyList<OfflinePublisherRegistration> ReadModernRegistrations() => _catalog.ReadRegistrations(_softwareHive.Root);

    /// <summary>Builds a pure-legacy provider (no WEVT manifest) from the image's <c>SYSTEM</c> hive registration.</summary>
    public ProviderDetails? TryBuildLegacyProvider(string providerName) => _legacyBuilder.TryBuild(providerName);

    /// <summary>
    ///     Builds a modern provider from its registration, resolving its WEVT manifest and message tables from the image.
    ///     Returns <see langword="null" /> when the registration has no resource file or the manifest cannot be parsed.
    /// </summary>
    public ProviderDetails? TryBuildModernProvider(OfflinePublisherRegistration registration)
    {
        if (string.IsNullOrEmpty(registration.ResourceFilePath))
        {
            _logger?.Debug($"{nameof(OfflineImageProviderExtractor)}: publisher {registration.ProviderName} has no resource file; skipping modern build.");

            return null;
        }

        return OfflineWevtProviderReader.TryBuildProviderDetails(
            registration.ResourceFilePath,
            registration.MessageFilePaths,
            registration.ParameterFilePath,
            registration.PublisherGuid,
            registration.ProviderName,
            _legacyResolver,
            _logger);
    }

    // Loads one hive, logging the specific failure reason at Error so a non-elevated user reading a real (dirty) image
    // sees the actionable "re-run as administrator" message rather than a silent generic failure.
    private static OfflineRegistryHive? LoadHive(string hivePath, string hiveName, ITraceLogger? logger)
    {
        OfflineHiveLoadResult result = OfflineRegistryHive.TryLoad(hivePath, logger);

        switch (result.Status)
        {
            case OfflineHiveLoadStatus.Loaded:
                return result.Hive;
            case OfflineHiveLoadStatus.NeedsElevation:
                logger?.Error($"The image's {hiveName} hive needs registry recovery, which requires running as administrator. Re-run elevated.");

                return null;
            case OfflineHiveLoadStatus.RecoveryFailed:
                logger?.Error($"The image's {hiveName} hive could not be recovered; it may be corrupt or missing its transaction logs.");

                return null;
            default:
                logger?.Error($"The image's {hiveName} hive is missing or is not a registry hive.");

                return null;
        }
    }
}
