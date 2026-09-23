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

    [Fact]
    public void RunButton_WhenDisabled_ExplainsTheFirstUnmetPrerequisite()
    {
        var component = Render<DiffDatabasesTab>();

        var runButton = component.Find(".button-green");
        Assert.True(runButton.HasAttribute("disabled"));
        Assert.False(runButton.HasAttribute("title"));
        Assert.Equal("[[Db_Diff_RunDisabled_NoFirst]]", runButton.GetAttribute("data-tooltip"));
        Assert.Equal("diff-run-disabled-help", runButton.GetAttribute("aria-describedby"));
        Assert.Equal("[[Db_Diff_RunDisabled_NoFirst]]", component.Find("#diff-run-disabled-help").TextContent);

        // The reason advances to the next unmet prerequisite as earlier fields are filled.
        component.Find("#diff-first-path").Input(@"C:\a.db");
        Assert.Equal("[[Db_Diff_RunDisabled_NoSecond]]", component.Find(".button-green").GetAttribute("data-tooltip"));

        // Invariant: once every prerequisite is met the button enables and carries NO disabled-reason attributes.
        component.Find("#diff-second-path").Input(@"C:\b.db");
        component.Find("#diff-new-db").Input(@"C:\out.db");
        var enabledRunButton = component.Find(".button-green");
        Assert.False(enabledRunButton.HasAttribute("disabled"));
        Assert.False(enabledRunButton.HasAttribute("data-tooltip"));
        Assert.False(enabledRunButton.HasAttribute("aria-describedby"));
    }
}
