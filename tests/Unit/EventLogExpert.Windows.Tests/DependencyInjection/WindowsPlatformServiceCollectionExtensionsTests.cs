// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Common.Identity;
using EventLogExpert.Runtime.Common.Restart;
using EventLogExpert.Runtime.DatabaseTools.Elevation;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace EventLogExpert.Windows.Tests.DependencyInjection;

public sealed class WindowsPlatformServiceCollectionExtensionsTests
{
    [Theory]
    [InlineData(typeof(IApplicationRestartService))]
    [InlineData(typeof(IWindowsIdentityProvider))]
    [InlineData(typeof(IElevatedHelperProcessHost))]
    public void AddWindowsPlatformAdapters_ShouldResolveHostFacingAbstraction(Type serviceType)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ITraceLogger>());
        services.AddWindowsPlatformAdapters();

        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        using var scope = provider.CreateScope();

        var resolved = scope.ServiceProvider.GetService(serviceType);

        Assert.NotNull(resolved);
    }
}
