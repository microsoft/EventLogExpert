// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Lookup;

namespace EventLogExpert.ProviderDatabase.Context;

public sealed class ProviderDbContextFactory : IProviderDetailsLookupFactory
{
    private readonly ITraceLogger? _logger;

    public ProviderDbContextFactory(ITraceLogger? logger = null) => _logger = logger;

    public IProviderDetailsLookup Create(string path) =>
        new ProviderDbContext(path, true, false, _logger);
}
