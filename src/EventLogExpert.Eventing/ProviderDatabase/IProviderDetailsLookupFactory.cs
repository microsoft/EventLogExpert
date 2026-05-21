// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Logging;

namespace EventLogExpert.Eventing.ProviderDatabase;

public interface IProviderDetailsLookupFactory
{
    IProviderDetailsLookup Create(string path, ITraceLogger? logger = null);
}
