// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.PublisherMetadata;

/// <summary>
///     Resolves the legacy (pre-manifest) message/category file paths registered for a provider under
///     <c>SYSTEM\…\Services\EventLog</c>. The live implementation (<see cref="RegistryProvider" />) reads the host
///     <c>HKLM\SYSTEM</c>; the offline implementation reads a foreign image's <c>SYSTEM</c> hive. Parameterizing the
///     legacy lookup lets the shared <see cref="Wevt.OfflineWevtProviderReader" /> populate legacy tables without a
///     host-registry dependency, so an offline image build never reads host data.
/// </summary>
internal interface ILegacyMessageFileResolver
{
    /// <summary>
    ///     Returns the legacy message/category file paths registered for <paramref name="providerName" />, or an empty
    ///     list when the provider has no legacy registration. Paths are already fully resolved (host-expanded for the live
    ///     source, re-rooted onto the image for the offline source) so the caller can open them directly.
    /// </summary>
    IReadOnlyList<string> GetMessageFilesForLegacyProvider(string providerName);
}
