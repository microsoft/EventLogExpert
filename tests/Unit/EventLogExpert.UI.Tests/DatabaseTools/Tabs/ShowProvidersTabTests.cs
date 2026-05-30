// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.UI.DatabaseTools.Tabs;
using EventLogExpert.UI.Tests.TestUtils;

namespace EventLogExpert.UI.Tests.DatabaseTools.Tabs;

public sealed class ShowProvidersTabTests : BunitContext
{
    public ShowProvidersTabTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddDatabaseToolsTabDependencies();
        Services.AddMenuMocks();
    }

    [Fact]
    public void Renders_FilterErrorState_WhenInvalidRegexEntered()
    {
        var component = Render<ShowProvidersTab>();

        var filter = component.Find("#show-filter");
        filter.Input("[unterminated");

        Assert.NotEmpty(component.FindAll(".filter-error"));
    }

    [Fact]
    public void Renders_HappyPath_WithExpectedFormFields()
    {
        var component = Render<ShowProvidersTab>();

        Assert.NotNull(component.Find("#show-source-path"));
        Assert.NotNull(component.Find("#show-filter"));
        Assert.NotNull(component.Find(".db-tab-form"));
    }

    [Fact]
    public void Renders_RunButton_PresentInitially_AndNotInCancellingState()
    {
        var component = Render<ShowProvidersTab>();

        Assert.NotNull(component.Find(".button-green"));
        Assert.Empty(component.FindAll(".button-red"));
    }
}
