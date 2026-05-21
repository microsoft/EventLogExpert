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
        var services = new ServiceCollection();
        services.AddEventLogFiltering();
        using var provider = services.BuildServiceProvider();

        var resolved = provider.GetService(serviceType);

        Assert.NotNull(resolved);
    }
}
