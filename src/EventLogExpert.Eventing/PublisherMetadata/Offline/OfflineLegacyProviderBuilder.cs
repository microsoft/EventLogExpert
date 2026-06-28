// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Resolution;

namespace EventLogExpert.Eventing.PublisherMetadata.Offline;

/// <summary>
///     Builds a <see cref="ProviderDetails" /> for a provider that has only a legacy (pre-manifest) registration - no
///     WEVT manifest - so it would otherwise be dropped (the modern reader's legacy population runs only AFTER a manifest
///     parse succeeds). Resolution goes through <see cref="OfflineLegacyMessageFileResolver" /> and
///     <see cref="LegacyMessageFileSource" />; it deliberately does NOT touch <see cref="EventMessageProvider" />, whose
///     static initializer enumerates host providers.
/// </summary>
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
