// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.OfflineImaging.Containment;
using EventLogExpert.Eventing.OfflineImaging.Registry;
using EventLogExpert.Eventing.ProviderMetadata.Wevt;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Resolution;

namespace EventLogExpert.Eventing.OfflineImaging.Extraction;

// Offline builds must re-root and guard-check every path so they never read host registry or host files.
internal sealed class OfflineImageProviderExtractor : IOfflineImageProviderExtractor
{
    private readonly OfflinePublisherCatalog _catalog;
    private readonly OfflineLegacyProviderBuilder _legacyBuilder;
    private readonly OfflineLegacyMessageFileResolver _legacyResolver;
    private readonly ITraceLogger? _logger;
    private readonly OfflineHiveFile _softwareHive;
    private readonly OfflineHiveFile _systemHive;

    private OfflineImageProviderExtractor(
        OfflineHiveFile softwareHive,
        OfflineHiveFile systemHive,
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

    public static OfflineImageProviderExtractor? TryCreate(OfflineImageRoot imageRoot, ITraceLogger? logger)
    {
        // Guard hive paths before opening so a config junction cannot redirect reads outside the image.
        var guard = new OfflineRootGuard(imageRoot, logger);
        guard.Assert(imageRoot.SoftwareHivePath, "SOFTWARE hive");
        guard.Assert(imageRoot.SystemHivePath, "SYSTEM hive");

        // Raw hive parsing logs under Offline.Hive; extraction orchestration keeps the caller's Offline.Providers logger.
        ITraceLogger? hiveLogger = logger?.ForCategory(LogCategories.OfflineHive);

        OfflineHiveFile? softwareHive = OfflineHiveFile.TryOpen(imageRoot.SoftwareHivePath, hiveLogger);

        if (softwareHive is null) { return null; }

        OfflineHiveFile? systemHive = OfflineHiveFile.TryOpen(imageRoot.SystemHivePath, hiveLogger);

        if (systemHive is null)
        {
            softwareHive.Dispose();

            return null;
        }

        var pathResolver = new OfflineImagePathResolver(new OfflineImagePathMapper(imageRoot, logger), guard);
        var catalog = new OfflinePublisherCatalog(pathResolver, logger);
        var legacyResolver = new OfflineLegacyMessageFileResolver(systemHive, pathResolver, logger);
        var legacyBuilder = new OfflineLegacyProviderBuilder(legacyResolver, logger);

        return new OfflineImageProviderExtractor(softwareHive, systemHive, catalog, legacyResolver, legacyBuilder, logger);
    }

    public void Dispose()
    {
        _systemHive.Dispose();
        _softwareHive.Dispose();
    }

    public IReadOnlyList<string> EnumerateLegacyProviderNames() => _legacyResolver.EnumerateProviderNames();

    public SourceOsProvenance ReadImageProvenance() => SourceOsProvenance.ReadFromSoftwareHive(_softwareHive, _logger);

    public IReadOnlyList<OfflinePublisherRegistration> ReadModernRegistrations() => _catalog.ReadRegistrations(_softwareHive);

    public ProviderDetails? TryBuildLegacyProvider(string providerName) => _legacyBuilder.TryBuild(providerName);

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
}
