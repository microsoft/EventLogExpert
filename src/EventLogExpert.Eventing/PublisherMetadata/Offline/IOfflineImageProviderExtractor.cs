// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Provider.Resolution;

namespace EventLogExpert.Eventing.PublisherMetadata.Offline;

/// <summary>
///     The offline image-extraction surface the public <c>OfflineImageProviderSource</c> facade orchestrates over.
///     Extracted as an interface so the facade's enumeration / de-dup / provenance-stamping logic can be unit-tested with
///     a fake extractor - real WEVT and message-table builds need real DLLs and only run against a real image.
/// </summary>
internal interface IOfflineImageProviderExtractor : IDisposable
{
    /// <summary>Distinct legacy provider names registered under the image's <c>SYSTEM</c> hive.</summary>
    IReadOnlyList<string> EnumerateLegacyProviderNames();

    /// <summary>Reads the image's OS provenance (build / revision / edition / display version) from its <c>SOFTWARE</c> hive.</summary>
    SourceOsProvenance ReadImageProvenance();

    /// <summary>The modern (manifest) publisher registrations in the image's <c>SOFTWARE</c> hive.</summary>
    IReadOnlyList<OfflinePublisherRegistration> ReadModernRegistrations();

    /// <summary>Builds a pure-legacy provider, or <see langword="null" /> when it has no usable legacy message files.</summary>
    ProviderDetails? TryBuildLegacyProvider(string providerName);

    /// <summary>Builds a modern provider from its registration, or <see langword="null" /> when its manifest cannot be parsed.</summary>
    ProviderDetails? TryBuildModernProvider(OfflinePublisherRegistration registration);
}
