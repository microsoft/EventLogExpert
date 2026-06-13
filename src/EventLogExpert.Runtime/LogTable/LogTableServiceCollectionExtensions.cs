// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;

namespace EventLogExpert.Runtime.LogTable;

public static class LogTableServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddColumnResetMigration()
        {
            ArgumentNullException.ThrowIfNull(services);

            if (ColumnResetMigrationFeature.IsEnabled)
            {
                services.AddSingleton<IColumnResetMigrator, ColumnResetMigrator>();
            }
            else
            {
                services.AddSingleton<IColumnResetMigrator, NoOpColumnResetMigrator>();
            }

            return services;
        }
    }
}
