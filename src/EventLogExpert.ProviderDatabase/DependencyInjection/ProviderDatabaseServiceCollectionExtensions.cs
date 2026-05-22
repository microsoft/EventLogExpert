// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.ProviderDatabase;
using EventLogExpert.ProviderDatabase;

namespace Microsoft.Extensions.DependencyInjection;

public static class ProviderDatabaseServiceCollectionExtensions
{
    public static IServiceCollection AddEventLogProviderDatabase(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IProviderDetailsLookupFactory, ProviderDbContextFactory>();
        services.AddSingleton<IProviderDatabaseMaintenance, ProviderDatabaseMaintenance>();

        return services;
    }
}
