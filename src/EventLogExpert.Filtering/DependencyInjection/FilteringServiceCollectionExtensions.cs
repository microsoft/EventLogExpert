// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Services;

namespace Microsoft.Extensions.DependencyInjection;

public static class FilteringServiceCollectionExtensions
{
    public static IServiceCollection AddEventLogFiltering(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IFilterService, FilterService>();

        return services;
    }
}
