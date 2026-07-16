// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Keyboard;
using EventLogExpert.UI.LogTable.Find;
using EventLogExpert.UI.Menu;

namespace Microsoft.Extensions.DependencyInjection;

public static class UiServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>Registers the EventLogExpert UI-library services into the host container.</summary>
        /// <remarks>
        ///     The registered <see cref="KeyboardShortcutService" /> resolves <c>IMenuActionService</c>,
        ///     <c>IModalCoordinator</c>, and <c>ISettingsService</c> from the container - the host must register those
        ///     abstractions (the Runtime layer via <c>AddEventLogRuntime</c> plus the host's menu adapter) before resolving UI
        ///     services.
        /// </remarks>
        public IServiceCollection AddEventLogUiServices()
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddSingleton<IMenuHostRegistry, MenuHostRegistry>();
            services.AddSingleton<IFindCoordinator, FindCoordinator>();
            services.AddSingleton<IFindMarkerSource, FindMarkerSource>();
            services.AddSingleton<KeyboardShortcutService>();

            return services;
        }
    }
}
