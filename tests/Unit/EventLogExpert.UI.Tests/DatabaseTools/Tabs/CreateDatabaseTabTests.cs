// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.UI.DatabaseTools.Tabs;
using EventLogExpert.UI.Tests.TestUtils;

namespace EventLogExpert.UI.Tests.DatabaseTools.Tabs;

public sealed class CreateDatabaseTabTests : BunitContext
{
    public CreateDatabaseTabTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddDatabaseToolsTabDependencies();
        Services.AddMenuMocks();
    }

    [Fact]
    public void Renders_FilterErrorState_WhenInvalidRegexEntered()
    {
        var component = Render<CreateDatabaseTab>();

        var filter = component.Find("#create-filter");
        filter.Input("[unterminated");

        Assert.NotEmpty(component.FindAll(".filter-error"));
    }

    [Fact]
    public void Renders_HappyPath_WithExpectedFormFields()
    {
        var component = Render<CreateDatabaseTab>();

        Assert.NotNull(component.Find("#create-target-path"));
        Assert.NotNull(component.Find("#create-source-path"));
        Assert.NotNull(component.Find("#create-filter"));
        Assert.NotNull(component.Find("#create-skip-path"));
    }

    [Fact]
    public void RunButton_DisabledInitially_BecauseTargetPathIsEmpty()
    {
        var component = Render<CreateDatabaseTab>();

        var runButton = component.Find(".button-green");
        Assert.True(runButton.HasAttribute("disabled"));
    }
}
