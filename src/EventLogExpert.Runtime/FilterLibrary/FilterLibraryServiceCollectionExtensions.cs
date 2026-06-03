// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace EventLogExpert.Runtime.FilterLibrary;

public static class FilterLibraryServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        ///     Registers the SQLite-backed <see cref="IFilterLibraryStore" /> implementation at <paramref name="dbPath" />.
        ///     The concrete <c>FilterLibrarySqliteStore</c> type stays internal to the runtime assembly; consumers depend on
        ///     <see cref="IFilterLibraryStore" /> only.
        /// </summary>
        public IServiceCollection AddFilterLibrarySqliteStore(string dbPath)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(dbPath);

            services.AddSingleton<IFilterLibraryStore>(sp =>
                new FilterLibrarySqliteStore(dbPath, sp.GetRequiredService<ITraceLogger>()));

            return services;
        }

        /// <summary>
        ///     Registers the legacy-filter migration singleton. When <see cref="LegacyMigrationFeature.Enabled" /> is
        ///     <see langword="true" /> (default), the real <see cref="LegacyFilterMigrator" /> is registered and the host MUST
        ///     also register <see cref="ILegacyPreferences" />. When <see langword="false" />, a
        ///     <see cref="NoOpLegacyFilterMigrator" /> is registered instead and <see cref="ILegacyPreferences" /> is not
        ///     required.
        /// </summary>
        public IServiceCollection AddLegacyFilterMigration()
        {
            ArgumentNullException.ThrowIfNull(services);

            if (LegacyMigrationFeature.Enabled)
            {
                services.AddSingleton<ILegacyFilterMigrator, LegacyFilterMigrator>();
            }
            else
            {
                services.AddSingleton<ILegacyFilterMigrator, NoOpLegacyFilterMigrator>();
            }

            return services;
        }
    }
}
