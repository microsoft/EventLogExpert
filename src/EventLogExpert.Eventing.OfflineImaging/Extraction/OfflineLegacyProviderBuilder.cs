// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.ProviderMetadata;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Resolution;

namespace EventLogExpert.Eventing.OfflineImaging.Extraction;

// Avoid EventMessageProvider here: its static initializer enumerates host providers.
internal sealed class OfflineLegacyProviderBuilder(OfflineLegacyMessageFileResolver legacyResolver, ITraceLogger? logger)
{
    public ProviderDetails? TryBuild(string providerName)
    {
        OfflineLegacyMessageFileResolver.LegacyProviderFiles files = legacyResolver.ResolveFilesForLegacyProvider(providerName);

        if (LegacyMessageSourceFactory.TryCreate(files.MessageFiles, providerName, logger) is not { } messageSource)
        {
            logger?.Debug($"{nameof(OfflineLegacyProviderBuilder)}: no usable legacy message files for provider {providerName}.");

            return null;
        }

        var details = new ProviderDetails { ProviderName = providerName };
        details.SetLazyMessageSource(messageSource);

        if (LegacyMessageSourceFactory.TryCreate(files.ParameterFiles, providerName, logger) is { } parameterSource)
        {
            details.SetLazyParameterSource(parameterSource);
        }

        return details;
    }
}
