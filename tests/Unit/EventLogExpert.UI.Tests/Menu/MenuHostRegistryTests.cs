// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Menu;
using EventLogExpert.Runtime.Modal;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.UI.Keyboard;
using EventLogExpert.UI.Menu;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventLogExpert.UI.Tests.Menu;

public sealed class MenuHostRegistryTests
{
    [Fact]
    public void ActiveHost_OnEmpty_IsNull()
    {
        var registry = new MenuHostRegistry();

        Assert.Null(registry.ActiveHost);
    }

    [Fact]
    public void ActiveHostChanged_FiresOnEachRegisterAndUnregister()
    {
        var registry = new MenuHostRegistry();
        var host = new MenuHost();

        int eventCount = 0;
        registry.ActiveHostChanged += () => eventCount++;

        registry.Register(host);
        Assert.Equal(1, eventCount);

        registry.Unregister(host);
        Assert.Equal(2, eventCount);
    }

    [Fact]
    public void AddEventLogUiServices_RegistersIMenuHostRegistry()
    {
        var services = new ServiceCollection();
        services.AddEventLogUiServices();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IMenuHostRegistry>();

        Assert.IsType<MenuHostRegistry>(registry);
    }

    [Fact]
    public async Task AddEventLogUiServices_RegistersKeyboardShortcutService()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IMenuActionService>());
        services.AddSingleton(Substitute.For<IModalCoordinator>());
        services.AddSingleton(Substitute.For<ISettingsService>());
        services.AddEventLogUiServices();

        await using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<KeyboardShortcutService>();

        Assert.NotNull(service);
    }

    [Fact]
    public void Register_FirstHost_BecomesActive()
    {
        var registry = new MenuHostRegistry();
        var host = new MenuHost();

        registry.Register(host);

        Assert.Same(host, registry.ActiveHost);
    }

    [Fact]
    public void Register_SecondHost_BecomesActive_StackPattern()
    {
        var registry = new MenuHostRegistry();
        var host1 = new MenuHost();
        var host2 = new MenuHost();

        registry.Register(host1);
        registry.Register(host2);

        Assert.Same(host2, registry.ActiveHost);
    }

    [Fact]
    public void Unregister_HostNotInStack_NoOp_NoEvent()
    {
        var registry = new MenuHostRegistry();
        var host1 = new MenuHost();
        var host2 = new MenuHost();
        registry.Register(host1);

        int eventCount = 0;
        registry.ActiveHostChanged += () => eventCount++;

        registry.Unregister(host2);

        Assert.Equal(0, eventCount);
        Assert.Same(host1, registry.ActiveHost);
    }

    [Fact]
    public void Unregister_NonTopmostHost_RemovesFromMiddle_KeepsTopmost()
    {
        var registry = new MenuHostRegistry();
        var host1 = new MenuHost();
        var host2 = new MenuHost();
        var host3 = new MenuHost();
        registry.Register(host1);
        registry.Register(host2);
        registry.Register(host3);

        registry.Unregister(host2);

        Assert.Same(host3, registry.ActiveHost);

        registry.Unregister(host3);
        Assert.Same(host1, registry.ActiveHost);
    }

    [Fact]
    public void Unregister_TopmostHost_RestoresPrevious()
    {
        var registry = new MenuHostRegistry();
        var host1 = new MenuHost();
        var host2 = new MenuHost();
        registry.Register(host1);
        registry.Register(host2);

        registry.Unregister(host2);

        Assert.Same(host1, registry.ActiveHost);
    }
}
