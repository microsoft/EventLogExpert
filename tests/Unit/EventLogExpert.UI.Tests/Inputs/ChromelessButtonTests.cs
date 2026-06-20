// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.UI.Inputs;

namespace EventLogExpert.UI.Tests.Inputs;

public sealed class ChromelessButtonTests : BunitContext
{
    [Fact]
    public void Render_EmptyCssClass_OmitsClassAttribute()
    {
        var component = Render<ChromelessButton>();

        var button = component.Find("button");
        Assert.False(button.HasAttribute("class"));
    }

    [Fact]
    public void Render_WithCssClass_RendersCssClassWithoutButtonChrome()
    {
        var component = Render<ChromelessButton>(parameters => parameters
            .Add(p => p.CssClass, "empty-dashboard__launch"));

        var button = component.Find("button");
        Assert.Contains("empty-dashboard__launch", button.ClassList);
        Assert.DoesNotContain("button", button.ClassList);
    }
}
