// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.UI.DatabaseTools.Tabs;
using EventLogExpert.UI.Tests.TestUtils;

namespace EventLogExpert.UI.Tests.DatabaseTools.Tabs;

public sealed class DiffDatabasesTabTests : BunitContext
{
    public DiffDatabasesTabTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddDatabaseToolsTabDependencies();
        Services.AddMenuMocks();
    }

    [Fact]
    public void Renders_AllThreePathInputs_RequiredForRun()
    {
        var component = Render<DiffDatabasesTab>();

        Assert.NotNull(component.Find("#diff-first-path"));
        Assert.NotNull(component.Find("#diff-second-path"));
        Assert.NotNull(component.Find("#diff-new-db"));
    }

    [Fact]
    public void Renders_HappyPath_WithExpectedFormFields()
    {
        var component = Render<DiffDatabasesTab>();

        Assert.NotNull(component.Find("#diff-first-path"));
        Assert.NotNull(component.Find("#diff-second-path"));
    }

    [Fact]
    public void RunButton_DisabledInitially_WhenInputsEmpty()
    {
        var component = Render<DiffDatabasesTab>();

        var runButton = component.Find(".button-green");
        Assert.True(runButton.HasAttribute("disabled"));
    }
}
