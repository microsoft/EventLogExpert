// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.ProviderMetadata;

public interface ILegacyMessageFileResolver
{
    IReadOnlyList<string> GetMessageFilesForLegacyProvider(string providerName);
}
