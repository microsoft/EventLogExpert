// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.UI.DatabaseTools.Tabs;
using EventLogExpert.UI.Tests.TestUtils;

namespace EventLogExpert.UI.Tests.DatabaseTools.Tabs;

public sealed class UpgradeDatabaseTabTests : BunitContext
{
    public UpgradeDatabaseTabTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddDatabaseToolsTabDependencies();
        Services.AddMenuMocks();
    }

    [Fact]
    public void Renders_HappyPath_WithExpectedFormFields()
    {
        var component = Render<UpgradeDatabaseTab>();

        Assert.NotNull(component.Find("#upgrade-db-path"));
    }

    [Fact]
    public void RunButton_DisabledInitially_WhenDbPathEmpty()
    {
        var component = Render<UpgradeDatabaseTab>();

        var runButton = component.Find(".button-green");
        Assert.True(runButton.HasAttribute("disabled"));
    }
}
