// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Components.Menu;
using EventLogExpert.Runtime.Menu;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Components.Tests.Menu;

public sealed class MenuRendererTests : BunitContext
{
    public MenuRendererTests()
    {
        // MenuRenderer issues `JSRuntime.InvokeVoidAsync("focusElement", ...)` after render to move
        // DOM focus; loose mode no-ops the call so the assertion can run synchronously.
        JSInterop.Mode = JSRuntimeMode.Loose;
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
        // Without a reason the item must not steal initial focus from the next focusable entry.
        Assert.Equal("-1", disabled.GetAttribute("tabindex"));

        // Initial focus lands on the next focusable item, not the silently-disabled one.
        var enabled = component.FindAll("li.menu-item")[1];
        Assert.Equal("0", enabled.GetAttribute("tabindex"));
    }

    [Fact]
    public void MenuRenderer_WithDisabledItemAndReason_AnnouncesReasonAndParticipatesInRovingFocus()
    {
        const string reason = "No cached filters yet — apply a Basic or Advanced filter to populate.";
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

        // Hidden span carries the reason text and matches the aria-describedby id so screen readers
        // announce the explanation when keyboard focus reaches the entry.
        var hiddenSpan = disabled.QuerySelector($"span#{describedBy}");
        Assert.NotNull(hiddenSpan);
        Assert.Contains("visually-hidden", hiddenSpan!.ClassName ?? string.Empty);
        Assert.Equal(reason, hiddenSpan.TextContent);

        // Informative-disabled item is the first focusable entry, so initial focus lands there
        // (sighted users see the title tooltip; AT users hear "Cached, dimmed, <reason>").
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
