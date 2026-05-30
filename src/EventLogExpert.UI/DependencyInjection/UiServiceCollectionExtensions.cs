// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Menu;

namespace Microsoft.Extensions.DependencyInjection;

public static class UiServiceCollectionExtensions
{
    public static IServiceCollection AddEventLogUiServices(this IServiceCollection services)
    {
        services.AddSingleton<IMenuHostRegistry, MenuHostRegistry>();

        return services;
    }
}
