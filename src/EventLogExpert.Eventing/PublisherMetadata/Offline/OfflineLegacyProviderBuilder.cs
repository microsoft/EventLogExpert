// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Resolution;

namespace EventLogExpert.Eventing.PublisherMetadata.Offline;

// Avoid EventMessageProvider here: its static initializer enumerates host providers.
internal sealed class OfflineLegacyProviderBuilder(OfflineLegacyMessageFileResolver legacyResolver, ITraceLogger? logger)
{
    public ProviderDetails? TryBuild(string providerName)
    {
        IReadOnlyList<string> messageFiles = legacyResolver.GetMessageFilesForLegacyProvider(providerName);

        if (LegacyMessageFileSource.TryCreate(messageFiles, providerName, logger) is not { } messageSource)
        {
            logger?.Debug($"{nameof(OfflineLegacyProviderBuilder)}: no usable legacy message files for provider {providerName}.");

            return null;
        }

        var details = new ProviderDetails { ProviderName = providerName };
        details.SetLazyMessageSource(messageSource);

        IReadOnlyList<string> parameterFiles = legacyResolver.GetParameterFilesForLegacyProvider(providerName);

        if (LegacyMessageFileSource.TryCreate(parameterFiles, providerName, logger) is { } parameterSource)
        {
            details.SetLazyParameterSource(parameterSource);
        }

        return details;
    }
}
