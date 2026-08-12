// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Menu;
using EventLogExpert.UI.Modal;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventLogExpert.UI.Tests.DependencyInjection;

public sealed class UIServiceCollectionExtensionsTests
{
    public static TheoryData<Type> RelocatedServiceTypes =>
        [typeof(IMenuService), typeof(IModalCoordinator), typeof(IModalService)];

    [Fact]
    public void AddEventLogUIServices_KeepsImplementationsTheHostAlreadySupplied()
    {
        IMenuService menuService = Substitute.For<IMenuService>();
        IModalCoordinator modalCoordinator = Substitute.For<IModalCoordinator>();
        IModalService modalService = Substitute.For<IModalService>();

        ServiceCollection services = new();
        services.AddSingleton(menuService);
        services.AddSingleton(modalCoordinator);
        services.AddSingleton(modalService);

        services.AddEventLogUIServices();

        Assert.Same(menuService, SingleDescriptorFor<IMenuService>(services).ImplementationInstance);
        Assert.Same(modalCoordinator, SingleDescriptorFor<IModalCoordinator>(services).ImplementationInstance);
        Assert.Same(modalService, SingleDescriptorFor<IModalService>(services).ImplementationInstance);

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Same(menuService, provider.GetRequiredService<IMenuService>());
        Assert.Same(modalCoordinator, provider.GetRequiredService<IModalCoordinator>());
        Assert.Same(modalService, provider.GetRequiredService<IModalService>());
    }

    [Fact]
    public void AddEventLogUIServices_RegistersMenuAndModalImplementations()
    {
        ServiceCollection services = new();

        services.AddEventLogUIServices();

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.IsType<MenuService>(provider.GetRequiredService<IMenuService>());
        Assert.IsType<ModalService>(provider.GetRequiredService<IModalService>());
        Assert.IsType<ModalCoordinator>(provider.GetRequiredService<IModalCoordinator>());
    }

    [Theory]
    [MemberData(nameof(RelocatedServiceTypes))]
    public void AddEventLogUIServices_RegistersRelocatedServicesAsSingletons(Type serviceType)
    {
        ServiceCollection services = new();

        services.AddEventLogUIServices();

        ServiceDescriptor descriptor = Assert.Single(services, candidate => candidate.ServiceType == serviceType);

        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    private static ServiceDescriptor SingleDescriptorFor<TService>(IServiceCollection services) =>
        Assert.Single(services, candidate => candidate.ServiceType == typeof(TService));
}
