// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.UI.DatabaseTools.Tabs;
using EventLogExpert.UI.Tests.TestUtils;

namespace EventLogExpert.UI.Tests.DatabaseTools.Tabs;

public sealed class MergeDatabaseTabTests : BunitContext
{
    public MergeDatabaseTabTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddDatabaseToolsTabDependencies();
        Services.AddMenuMocks();
    }

    [Fact]
    public void Renders_HappyPath_WithExpectedFormFields()
    {
        var component = Render<MergeDatabaseTab>();

        Assert.NotNull(component.Find("#merge-source-path"));
        Assert.NotNull(component.Find("#merge-target-path"));
        Assert.NotEmpty(component.FindAll(".overwrite-mode-select"));
    }

    [Fact]
    public void RunButton_DisabledInitially_WhenSourceAndTargetEmpty()
    {
        var component = Render<MergeDatabaseTab>();

        var runButton = component.Find(".button-green");
        Assert.True(runButton.HasAttribute("disabled"));
    }
}
