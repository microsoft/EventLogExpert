// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Menu;
using EventLogExpert.UI.Menu;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventLogExpert.UI.Tests.TestUtils;

internal static class MenuHostDependenciesExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddMenuHostRegistryMock()
        {
            services.AddSingleton(Substitute.For<IMenuHostRegistry>());
            return services;
        }

        public IServiceCollection AddMenuMocks()
        {
            services.AddMenuServiceMock();
            services.AddMenuHostRegistryMock();
            return services;
        }

        public IServiceCollection AddMenuServiceMock()
        {
            services.AddSingleton(Substitute.For<IMenuService>());
            return services;
        }
    }
}
