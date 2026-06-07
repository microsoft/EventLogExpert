// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Common.Identity;
using EventLogExpert.Runtime.Common.Restart;
using EventLogExpert.Runtime.DatabaseTools.Elevation;
using EventLogExpert.WindowsPlatform.Elevation;
using EventLogExpert.WindowsPlatform.Identity;
using EventLogExpert.WindowsPlatform.Restart;

namespace Microsoft.Extensions.DependencyInjection;

public static class WindowsPlatformServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddWindowsPlatformAdapters()
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddSingleton<IApplicationRestartService, WindowsApplicationRestartService>();
            services.AddSingleton<IWindowsIdentityProvider, WindowsIdentityProvider>();
            services.AddSingleton<IElevatedHelperProcessHost, ElevatedHelperProcessHost>();

            return services;
        }
    }
}
