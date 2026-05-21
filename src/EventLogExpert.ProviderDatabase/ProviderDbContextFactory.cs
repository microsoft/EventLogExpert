// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Logging;

namespace EventLogExpert.Eventing.ProviderDatabase;

public sealed class ProviderDbContextFactory : IProviderDetailsLookupFactory
{
    public IProviderDetailsLookup Create(string path, ITraceLogger? logger = null) =>
        new ProviderDbContext(path, readOnly: true, ensureCreated: false, logger);
}
