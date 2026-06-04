// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.UI.Modal;

namespace EventLogExpert.UI.Tests.Modal;

public sealed class SidebarTabsFocusContractTests : BunitContext
{
    private static readonly IReadOnlyList<(TestTab Tab, string Label)> s_tabs =
    [
        (TestTab.One, "One"),
        (TestTab.Two, "Two"),
    ];

    public SidebarTabsFocusContractTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    public enum TestTab { One, Two }

    [Fact]
    public void IsTabpanelFocusable_AppliesToActiveOnly_NotToInactivePanels()
    {
        var component = Render<SidebarTabs<TestTab>>(parameters => parameters
            .Add(p => p.Tabs, s_tabs)
            .Add(p => p.ActiveTab, TestTab.One)
            .Add<Func<TestTab, bool>?>(p => p.IsTabpanelFocusable, _ => true)
            .Add(p => p.TabContent, _ => builder => { }));

        var panels = component.FindAll("[role='tabpanel']");
        Assert.Equal("0", panels[0].GetAttribute("tabindex"));
        Assert.Null(panels[1].GetAttribute("tabindex"));
    }

    [Fact]
    public void IsTabpanelFocusable_DefaultNull_DoesNotEmitTabindexOnActivePanel()
    {
        var component = Render<SidebarTabs<TestTab>>(parameters => parameters
            .Add(p => p.Tabs, s_tabs)
            .Add(p => p.ActiveTab, TestTab.One)
            .Add(p => p.TabContent, _ => builder => { }));

        var panels = component.FindAll("[role='tabpanel']");
        Assert.Null(panels[0].GetAttribute("tabindex"));
    }

    [Fact]
    public void IsTabpanelFocusable_ReturnsTrue_EmitsTabindex0OnActivePanel()
    {
        var component = Render<SidebarTabs<TestTab>>(parameters => parameters
            .Add(p => p.Tabs, s_tabs)
            .Add(p => p.ActiveTab, TestTab.One)
            .Add<Func<TestTab, bool>?>(p => p.IsTabpanelFocusable, _ => true)
            .Add(p => p.TabContent, _ => builder => { }));

        var panels = component.FindAll("[role='tabpanel']");
        Assert.Equal("0", panels[0].GetAttribute("tabindex"));
    }
}
