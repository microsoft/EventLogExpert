// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Logging;
using EventLogExpert.Eventing.ProviderDatabase;

namespace EventLogExpert.ProviderDatabase;

public sealed class ProviderDbContextFactory : IProviderDetailsLookupFactory
{
    public IProviderDetailsLookup Create(string path, ITraceLogger? logger = null) =>
        new ProviderDbContext(path, readOnly: true, ensureCreated: false, logger);
}
