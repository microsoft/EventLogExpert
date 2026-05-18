// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace Microsoft.Extensions.DependencyInjection;

public static class FilteringServiceCollectionExtensions
{
    public static IServiceCollection AddEventLogFiltering(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services;
    }
}
