// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Provider.Resolution;

namespace EventLogExpert.Eventing.OfflineImaging.Extraction;

internal interface IOfflineImageProviderExtractor : IDisposable
{
    IReadOnlyList<string> EnumerateLegacyProviderNames();

    SourceOsProvenance ReadImageProvenance();

    IReadOnlyList<OfflinePublisherRegistration> ReadModernRegistrations();

    ProviderDetails? TryBuildLegacyProvider(string providerName);

    ProviderDetails? TryBuildModernProvider(OfflinePublisherRegistration registration);
}
