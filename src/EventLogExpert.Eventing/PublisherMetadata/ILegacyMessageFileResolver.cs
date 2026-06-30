// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.PublisherMetadata;

internal interface ILegacyMessageFileResolver
{
    IReadOnlyList<string> GetMessageFilesForLegacyProvider(string providerName);
}
