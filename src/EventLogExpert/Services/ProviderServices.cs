// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventResolvers;
using EventLogExpert.UI.Services;

namespace EventLogExpert.Services;

public static class ProviderServices
{
    public static IServiceCollection AddProviderServices(this IServiceCollection services)
    {
        services.AddSingleton<IEnabledDatabaseCollectionProvider, EnabledDatabaseCollectionProvider>();
        services.AddSingleton<IDatabaseCollectionProvider, EnabledDatabaseCollectionProvider>();

        services.AddTransient<IEventResolver, VersatileEventResolver>();

        return services;
    }
}
