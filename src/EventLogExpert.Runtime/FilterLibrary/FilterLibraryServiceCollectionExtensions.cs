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
        ///     Registers <see cref="IFilterLibraryExportService" /> backed by the internal-sealed implementation. Keeps the
        ///     JSON-envelope and content-fingerprint logic encapsulated in the runtime assembly; UI consumers depend on the public
        ///     interface only.
        /// </summary>
        public IServiceCollection AddFilterLibraryExport()
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddSingleton<IFilterLibraryExportService, FilterLibraryExportService>();

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

        public IServiceCollection AddBackslashNameMigration()
        {
            ArgumentNullException.ThrowIfNull(services);

            if (BackslashMigrationFeature.Enabled)
            {
                services.AddSingleton<IBackslashNameMigrator, BackslashNameMigrator>();
            }
            else
            {
                services.AddSingleton<IBackslashNameMigrator, NoOpBackslashNameMigrator>();
            }

            return services;
        }
    }
}
