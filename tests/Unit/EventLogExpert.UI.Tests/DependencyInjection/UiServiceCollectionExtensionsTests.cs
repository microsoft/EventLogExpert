// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.DependencyInjection;
using EventLogExpert.UI.EventLog;
using EventLogExpert.UI.FilterGroup;
using EventLogExpert.UI.FilterPane;
using EventLogExpert.UI.LogTable;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventLogExpert.UI.Tests.DependencyInjection;

public sealed class UiServiceCollectionExtensionsTests
{
    [Theory]
    [InlineData(typeof(IEventLogCommands))]
    [InlineData(typeof(IFilterGroupCommands))]
    [InlineData(typeof(IFilterPaneCommands))]
    [InlineData(typeof(ILogTableCommands))]
    [InlineData(typeof(IHighlightFilterSelector))]
    [InlineData(typeof(ILogTableColumnDefaultsProvider))]
    public void RegisterUiLibrary_ShouldResolveHostFacingAbstraction(Type serviceType)
    {
        // Pins that RegisterUiLibrary() wires every host-facing intent / capability API the MAUI head
        // consumes — preventing a runtime composition failure after a future cleanup wave that moves
        // existing public impls behind the same registrar.
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IDispatcher>());
        services.RegisterUiLibrary();
        using var provider = services.BuildServiceProvider();

        var resolved = provider.GetService(serviceType);

        Assert.NotNull(resolved);
    }
}
