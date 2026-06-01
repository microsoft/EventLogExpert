// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace EventLogExpert.Runtime.FilterLibrary;

public static class FilterLibraryServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the SQLite-backed <see cref="IFilterLibraryStore" /> implementation at <paramref name="dbPath" />.
    ///     The concrete <c>FilterLibrarySqliteStore</c> type stays internal to the runtime assembly; consumers depend on
    ///     <see cref="IFilterLibraryStore" /> only.
    /// </summary>
    public static IServiceCollection AddFilterLibrarySqliteStore(this IServiceCollection services, string dbPath)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(dbPath);

        services.AddSingleton<IFilterLibraryStore>(sp =>
            new FilterLibrarySqliteStore(dbPath, sp.GetRequiredService<ITraceLogger>()));

        return services;
    }
}
