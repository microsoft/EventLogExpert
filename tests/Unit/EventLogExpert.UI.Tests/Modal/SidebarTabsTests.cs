// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.UI.Modal;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace EventLogExpert.UI.Tests.Modal;

public sealed class SidebarTabsTests : BunitContext
{
    private static readonly IReadOnlyList<(TestTab Tab, string Label)> s_tabs =
    [
        (TestTab.First, "First"),
        (TestTab.Second, "Second"),
        (TestTab.Third, "Third"),
    ];

    public SidebarTabsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    public enum TestTab { First, Second, Third }

    [Fact]
    public async Task OnTabActivated_FiresOnTabChange()
    {
        TestTab? fired = null;
        var component = Render<SidebarTabs<TestTab>>(parameters => parameters
            .Add(p => p.Tabs, s_tabs)
            .Add(p => p.ActiveTab, TestTab.First)
            .Add(p => p.OnTabActivated, EventCallback.Factory.Create<TestTab>(this, t => fired = t))
            .Add(p => p.TabContent, _ => builder => { }));

        await component.FindAll("[role='tab']")[1].ClickAsync(new MouseEventArgs());

        Assert.Equal(TestTab.Second, fired);
    }

    [Fact]
    public void Render_ActiveTab_HasTabindex0_InactiveTabs_HaveTabindexMinus1()
    {
        var component = Render<SidebarTabs<TestTab>>(parameters => parameters
            .Add(p => p.Tabs, s_tabs)
            .Add(p => p.ActiveTab, TestTab.Second)
            .Add(p => p.TabContent, _ => builder => { }));

        var tabs = component.FindAll("[role='tab']");
        Assert.Equal("-1", tabs[0].GetAttribute("tabindex"));
        Assert.Equal("0", tabs[1].GetAttribute("tabindex"));
        Assert.Equal("-1", tabs[2].GetAttribute("tabindex"));
    }

    [Fact]
    public void Render_AppliesAriaOrientationVerticalOnTablist()
    {
        var component = Render<SidebarTabs<TestTab>>(parameters => parameters
            .Add(p => p.Tabs, s_tabs)
            .Add(p => p.ActiveTab, TestTab.First)
            .Add(p => p.TabContent, _ => builder => { }));

        var tablist = component.Find("[role='tablist']");
        Assert.Equal("vertical", tablist.GetAttribute("aria-orientation"));
    }

    [Fact]
    public void Render_InactiveTabpanels_HaveDisplayNone()
    {
        var component = Render<SidebarTabs<TestTab>>(parameters => parameters
            .Add(p => p.Tabs, s_tabs)
            .Add(p => p.ActiveTab, TestTab.First)
            .Add(p => p.TabContent, _ => builder => { }));

        var panels = component.FindAll("[role='tabpanel']");
        Assert.Equal(3, panels.Count);
        Assert.DoesNotContain("display: none", panels[0].GetAttribute("style") ?? string.Empty);
        Assert.Contains("display: none", panels[1].GetAttribute("style") ?? string.Empty);
        Assert.Contains("display: none", panels[2].GetAttribute("style") ?? string.Empty);
    }

    [Fact]
    public void Render_TabContent_RendersOncePerTab_WithTabAsContext()
    {
        var renderedFor = new List<TestTab>();
        var component = Render<SidebarTabs<TestTab>>(parameters => parameters
            .Add(p => p.Tabs, s_tabs)
            .Add(p => p.ActiveTab, TestTab.First)
            .Add<RenderFragment<TestTab>>(p => p.TabContent, tab => builder =>
            {
                renderedFor.Add(tab);
                builder.AddContent(0, $"content-for-{tab}");
            }));

        Assert.Equal(3, renderedFor.Count);
        Assert.Contains("content-for-First", component.Markup);
        Assert.Contains("content-for-Second", component.Markup);
        Assert.Contains("content-for-Third", component.Markup);
    }

    [Fact]
    public async Task TabClick_RaisesActiveTabChanged_WithClickedTab()
    {
        TestTab? captured = null;
        var component = Render<SidebarTabs<TestTab>>(parameters => parameters
            .Add(p => p.Tabs, s_tabs)
            .Add(p => p.ActiveTab, TestTab.First)
            .Add(p => p.ActiveTabChanged, EventCallback.Factory.Create<TestTab>(this, t => captured = t))
            .Add(p => p.TabContent, _ => builder => { }));

        await component.FindAll("[role='tab']")[2].ClickAsync(new MouseEventArgs());

        Assert.Equal(TestTab.Third, captured);
    }

    [Theory]
    [InlineData("ArrowLeft")]
    [InlineData("ArrowRight")]
    public async Task TabKeydown_HorizontalArrows_AreIgnored(string key)
    {
        TestTab? captured = null;
        var component = Render<SidebarTabs<TestTab>>(parameters => parameters
            .Add(p => p.Tabs, s_tabs)
            .Add(p => p.ActiveTab, TestTab.First)
            .Add(p => p.ActiveTabChanged, EventCallback.Factory.Create<TestTab>(this, t => captured = t))
            .Add(p => p.TabContent, _ => builder => { }));

        await component.FindAll("[role='tab']")[0].KeyDownAsync(new KeyboardEventArgs { Key = key });

        Assert.Null(captured);
    }

    [Theory]
    [InlineData("ArrowDown", TestTab.Third)]
    [InlineData("ArrowUp", TestTab.First)]
    [InlineData("Home", TestTab.First)]
    [InlineData("End", TestTab.Third)]
    public async Task TabKeydown_VerticalKeys_RotateActiveTab(string key, TestTab expected)
    {
        TestTab? captured = null;
        var component = Render<SidebarTabs<TestTab>>(parameters => parameters
            .Add(p => p.Tabs, s_tabs)
            .Add(p => p.ActiveTab, TestTab.Second)
            .Add(p => p.ActiveTabChanged, EventCallback.Factory.Create<TestTab>(this, t => captured = t))
            .Add(p => p.TabContent, _ => builder => { }));

        await component.FindAll("[role='tab']")[1].KeyDownAsync(new KeyboardEventArgs { Key = key });

        Assert.Equal(expected, captured);
    }
}
