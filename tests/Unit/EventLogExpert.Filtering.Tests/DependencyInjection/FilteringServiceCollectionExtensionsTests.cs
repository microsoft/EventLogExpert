// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Compilation;
using Microsoft.Extensions.DependencyInjection;

namespace EventLogExpert.Filtering.Tests.DependencyInjection;

public sealed class FilteringServiceCollectionExtensionsTests
{
    [Theory]
    [InlineData(typeof(IFilterService))]
    public void AddEventLogFiltering_ShouldResolveHostFacingAbstraction(Type serviceType)
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddEventLogFiltering();

        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        using var scope = provider.CreateScope();

        // Act
        var resolved = scope.ServiceProvider.GetService(serviceType);

        // Assert
        Assert.NotNull(resolved);
    }
}
