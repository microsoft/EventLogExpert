// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.ProviderDatabase;
using Microsoft.Extensions.DependencyInjection;

namespace EventLogExpert.ProviderDatabase.Tests.DependencyInjection;

public sealed class ProviderDatabaseServiceCollectionExtensionsTests
{
    [Theory]
    [InlineData(typeof(IProviderDetailsLookupFactory))]
    [InlineData(typeof(IProviderDatabaseMaintenance))]
    public void AddEventLogProviderDatabase_ShouldResolveHostFacingAbstraction(Type serviceType)
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddEventLogProviderDatabase();

        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        using var scope = provider.CreateScope();

        // Act
        var resolved = scope.ServiceProvider.GetService(serviceType);

        // Assert
        Assert.NotNull(resolved);
    }
}
