// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Keyboard;
using EventLogExpert.UI.Menu;

namespace Microsoft.Extensions.DependencyInjection;

public static class UiServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>Registers the EventLogExpert UI-library services into the host container.</summary>
        /// <remarks>
        ///     The registered <see cref="KeyboardShortcutService" /> resolves <c>IMenuActionService</c>,
        ///     <c>IModalCoordinator</c>, and <c>ISettingsService</c> from the container — the host must also register the Runtime
        ///     layer (e.g. <c>AddEventLogRuntime</c>) and platform adapters, or resolving them throws.
        /// </remarks>
        public IServiceCollection AddEventLogUiServices()
        {
            services.AddSingleton<IMenuHostRegistry, MenuHostRegistry>();
            services.AddSingleton<KeyboardShortcutService>();

            return services;
        }
    }
}
