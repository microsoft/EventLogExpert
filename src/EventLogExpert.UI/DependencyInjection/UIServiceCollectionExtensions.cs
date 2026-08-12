// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Keyboard;
using EventLogExpert.UI.LogTable.Find;
using EventLogExpert.UI.Menu;
using EventLogExpert.UI.Modal;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class UIServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddEventLogUIServices()
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddSingleton<IMenuHostRegistry, MenuHostRegistry>();
            services.AddSingleton<IFindCoordinator, FindCoordinator>();
            services.AddSingleton<IFindMarkerSource, FindMarkerSource>();
            services.AddSingleton<KeyboardShortcutService>();

            services.TryAddSingleton<IMenuService, MenuService>();
            services.TryAddSingleton<IModalCoordinator, ModalCoordinator>();
            services.TryAddSingleton<IModalService, ModalService>();

            return services;
        }
    }
}
