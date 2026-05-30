// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Menu;

namespace Microsoft.Extensions.DependencyInjection;

public static class UiServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddEventLogUiServices()
        {
            services.AddSingleton<IMenuHostRegistry, MenuHostRegistry>();

            return services;
        }
    }
}
