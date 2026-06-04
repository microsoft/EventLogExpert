// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.UI.Modal;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace EventLogExpert.UI.Tests.Modal;

public sealed class SidebarTabsCascadeTests : BunitContext
{
    private static readonly IReadOnlyList<(TestTab Tab, string Label)> s_tabs =
    [
        (TestTab.A, "A"),
        (TestTab.B, "B"),
    ];

    public SidebarTabsCascadeTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    public enum TestTab { A, B }

    [Fact]
    public void CascadingValue_OutsideSidebarTabs_IsReachableInsideTabContent()
    {
        const string SentinelValue = "sentinel-from-outside";
        var component = Render<CascadeFixture>(parameters => parameters
            .Add(p => p.Value, SentinelValue)
            .Add(p => p.Tabs, s_tabs)
            .Add(p => p.ActiveTab, TestTab.A));

        Assert.Contains(SentinelValue, component.Markup);
    }

    public sealed class CascadeFixture : ComponentBase
    {
        [Parameter] public TestTab ActiveTab { get; set; }

        [Parameter] public IReadOnlyList<(TestTab Tab, string Label)> Tabs { get; set; } = [];

        [Parameter] public string Value { get; set; } = string.Empty;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<CascadingValue<string>>(0);
            builder.AddAttribute(1, "Value", Value);
            builder.AddAttribute(2, "ChildContent", (RenderFragment)(b =>
            {
                b.OpenComponent<SidebarTabs<TestTab>>(0);
                b.AddAttribute(1, "Tabs", Tabs);
                b.AddAttribute(2, "ActiveTab", ActiveTab);
                b.AddAttribute(3, "TabContent", (RenderFragment<TestTab>)(_ => fragmentBuilder =>
                {
                    fragmentBuilder.OpenComponent<CascadeProbe>(0);
                    fragmentBuilder.CloseComponent();
                }));
                b.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }

    public sealed class CascadeProbe : ComponentBase
    {
        [CascadingParameter] public string Received { get; set; } = string.Empty;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.AddContent(0, Received);
        }
    }
}
