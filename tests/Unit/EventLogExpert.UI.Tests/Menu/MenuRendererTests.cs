// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.UI.Menu;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.Tests.Menu;

public sealed class MenuRendererTests : BunitContext
{
    public MenuRendererTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddEventLogLocalization();
    }

    [Fact]
    public async Task AsyncSubmenu_WhileLoading_RendersLocalizedLoadingChrome()
    {
        var localizer = Services.GetRequiredService<IStringLocalizer<SharedResource>>();
        var gate = new TaskCompletionSource<IReadOnlyList<MenuItem>>();
        var items = new[] { MenuItem.AsyncSubMenu("Parent", () => gate.Task) };

        var component = Render<MenuRenderer>(parameters => parameters.Add(p => p.Items, items));

        var activateTask = component.Find("li.menu-item").ClickAsync(new());

        component.WaitForAssertion(() =>
        {
            var busy = component.Find("ul[aria-busy='true']");
            Assert.Equal(localizer["Menu_LoadingAria"].Value, busy.GetAttribute("aria-label"));
            Assert.Equal(localizer["Menu_Loading"].Value, busy.QuerySelector(".menu-label")!.TextContent);
        });

        gate.SetResult([]);
        await activateTask;
    }

    [Fact]
    public async Task MenuRenderer_DisabledItemActivation_DoesNotRaiseOnActivated()
    {
        bool actionInvoked = false;
        bool activated = false;

        var items = new[]
        {
            MenuItem.Item("Cached", () => actionInvoked = true, isEnabled: false, disabledReason: "Empty"),
        };

        var component = Render<MenuRenderer>(parameters => parameters
            .Add(p => p.Items, items)
            .Add(p => p.OnActivated, EventCallback.Factory.Create(this, () => activated = true)));

        await component.Find("li.menu-item").ClickAsync(new());

        Assert.False(actionInvoked);
        Assert.False(activated);
    }

    [Fact]
    public void MenuRenderer_WithDisabledItemAndNoReason_RendersInertEntryThatSkipsRovingFocus()
    {
        var items = new[]
        {
            MenuItem.Item("Open", () => { }, isEnabled: false),
            MenuItem.Item("Close", () => { }),
        };

        var component = Render<MenuRenderer>(parameters => parameters.Add(p => p.Items, items));

        var disabled = component.FindAll("li.menu-item")[0];
        Assert.Equal("true", disabled.GetAttribute("aria-disabled"));
        Assert.Null(disabled.GetAttribute("aria-describedby"));
        Assert.Null(disabled.GetAttribute("title"));
        Assert.Equal("-1", disabled.GetAttribute("tabindex"));

        var enabled = component.FindAll("li.menu-item")[1];
        Assert.Equal("0", enabled.GetAttribute("tabindex"));
    }

    [Fact]
    public void MenuRenderer_WithDisabledItemAndReason_AnnouncesReasonAndParticipatesInRovingFocus()
    {
        const string reason = "No cached filters yet - apply a Basic or Advanced filter to populate.";
        var items = new[]
        {
            MenuItem.Item("Cached", () => { }, isEnabled: false, disabledReason: reason),
            MenuItem.Item("Advanced", () => { }),
        };

        var component = Render<MenuRenderer>(parameters => parameters.Add(p => p.Items, items));

        var disabled = component.FindAll("li.menu-item")[0];
        Assert.Equal("true", disabled.GetAttribute("aria-disabled"));
        Assert.Equal(reason, disabled.GetAttribute("title"));

        var describedBy = disabled.GetAttribute("aria-describedby");
        Assert.False(string.IsNullOrEmpty(describedBy));
        Assert.StartsWith("menu-item-reason-", describedBy);

        var hiddenSpan = disabled.QuerySelector($"span#{describedBy}");
        Assert.NotNull(hiddenSpan);
        Assert.Contains("visually-hidden", hiddenSpan!.ClassName ?? string.Empty);
        Assert.Equal(reason, hiddenSpan.TextContent);

        Assert.Equal("0", disabled.GetAttribute("tabindex"));
        Assert.Equal("-1", component.FindAll("li.menu-item")[1].GetAttribute("tabindex"));
    }

    [Fact]
    public void MenuRenderer_WithEnabledItem_RendersFocusableMenuItemWithoutDisabledMarkup()
    {
        var items = new[] { MenuItem.Item("Open", () => { }) };

        var component = Render<MenuRenderer>(parameters => parameters.Add(p => p.Items, items));

        var listItem = component.Find("li.menu-item");
        Assert.Null(listItem.GetAttribute("aria-disabled"));
        Assert.Null(listItem.GetAttribute("aria-describedby"));
        Assert.Null(listItem.GetAttribute("title"));
        Assert.Equal("0", listItem.GetAttribute("tabindex"));
        Assert.Empty(listItem.QuerySelectorAll("span.visually-hidden"));
    }

    [Fact]
    public async Task MenuRenderer_WithEnabledStatus_RendersVisibleTagAndAllowsActivation()
    {
        bool actionInvoked = false;
        var items = new[]
        {
            MenuItem.Item("Operational", () => actionInvoked = true, statusText: "(disabled)"),
        };

        var component = Render<MenuRenderer>(parameters => parameters.Add(p => p.Items, items));

        await component.Find("li.menu-item").ClickAsync(new());

        Assert.True(actionInvoked);
        Assert.Equal("(disabled)", component.Find(".menu-status").TextContent);
        Assert.Null(component.Find("li.menu-item").GetAttribute("aria-disabled"));
    }

    [Fact]
    public void MenuRenderer_WithMultipleInformativeDisabledItems_GeneratesUniqueDescribedByIds()
    {
        var items = new[]
        {
            MenuItem.Item("First", () => { }, isEnabled: false, disabledReason: "Reason one"),
            MenuItem.Item("Second", () => { }, isEnabled: false, disabledReason: "Reason two"),
        };

        var component = Render<MenuRenderer>(parameters => parameters.Add(p => p.Items, items));

        var listItems = component.FindAll("li.menu-item");
        var firstId = listItems[0].GetAttribute("aria-describedby");
        var secondId = listItems[1].GetAttribute("aria-describedby");

        Assert.False(string.IsNullOrEmpty(firstId));
        Assert.False(string.IsNullOrEmpty(secondId));
        Assert.NotEqual(firstId, secondId);
    }
}
