// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace EventLogExpert.Runtime.FilterLibrary;

public static class FilterLibraryServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddFilterLibrarySqliteStore(string dbPath)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(dbPath);

            services.AddSingleton<IFilterLibraryStore>(sp =>
                new FilterLibrarySqliteStore(dbPath, sp.GetRequiredService<ITraceLogger>()));

            return services;
        }

        public IServiceCollection AddFilterLibraryExport()
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddSingleton<IFilterLibraryExportService, FilterLibraryExportService>();

            return services;
        }

        public IServiceCollection AddLegacyFilterMigration()
        {
            ArgumentNullException.ThrowIfNull(services);

            if (LegacyMigrationFeature.IsEnabled)
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

            if (BackslashMigrationFeature.IsEnabled)
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
