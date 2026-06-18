// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Scenarios.Catalog;
using EventLogExpert.UI.Dashboard;
using Microsoft.AspNetCore.Components.Web;

namespace EventLogExpert.UI.Tests.Dashboard;

public sealed class ScenarioBrowserPanelTests : BunitContext
{
    public ScenarioBrowserPanelTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    [Fact]
    public void Click_SelectsOption()
    {
        var scenarios = new[] { Scenario("first", "First"), Scenario("second", "Second") };
        ScenarioDefinition? selected = null;

        var cut = Render<ScenarioBrowserPanel>(parameters => parameters
            .Add(panel => panel.Scenarios, scenarios)
            .Add(panel => panel.Selected, scenarios[0])
            .Add(panel => panel.ElevationReasonId, "reason")
            .Add(panel => panel.IsFavored, _ => false)
            .Add(panel => panel.IsScenarioDisabled, _ => false)
            .Add(panel => panel.OnSelect, scenario => selected = scenario));

        cut.FindAll("[role='option']").First(option => option.TextContent.Contains("Second")).Click();

        cut.WaitForAssertion(() => Assert.Equal("second", selected?.Id));
    }

    [Fact]
    public void DisabledOption_IsSelectableButDoesNotLaunch()
    {
        var disabled = Scenario("locked", "Locked", requiresAdmin: true);
        bool launched = false;

        var cut = Render<ScenarioBrowserPanel>(parameters => parameters
            .Add(panel => panel.Scenarios, [disabled])
            .Add(panel => panel.Selected, disabled)
            .Add(panel => panel.ElevationReasonId, "reason")
            .Add(panel => panel.IsFavored, _ => false)
            .Add(panel => panel.IsScenarioDisabled, _ => true)
            .Add(panel => panel.OnLaunch, _ => launched = true));

        Assert.Equal("true", cut.Find("[role='option']").GetAttribute("aria-disabled"));

        cut.Find(".scenario-detail__launch").Click();

        Assert.False(launched);
    }

    [Fact]
    public void EmptyList_ShowsEmptyState()
    {
        var cut = Render<ScenarioBrowserPanel>(parameters => parameters
            .Add(panel => panel.Scenarios, [])
            .Add(panel => panel.ElevationReasonId, "reason")
            .Add(panel => panel.IsFavored, _ => false)
            .Add(panel => panel.IsScenarioDisabled, _ => false));

        Assert.NotEmpty(cut.FindAll(".scenario-browser__empty"));
        Assert.Empty(cut.FindAll(".scenario-detail"));
    }

    [Fact]
    public void Option_RendersNameOnly_WithPurposeInTitle()
    {
        var scenarios = new[] { Scenario("first", "First") };

        var cut = Render<ScenarioBrowserPanel>(parameters => parameters
            .Add(panel => panel.Scenarios, scenarios)
            .Add(panel => panel.Selected, scenarios[0])
            .Add(panel => panel.ElevationReasonId, "reason")
            .Add(panel => panel.IsFavored, _ => false)
            .Add(panel => panel.IsScenarioDisabled, _ => false));

        var option = cut.Find("[role='option']");

        Assert.Equal("First", option.QuerySelector(".scenario-browser__option-name")!.TextContent);
        Assert.Empty(option.QuerySelectorAll(".scenario-browser__option-purpose"));
        Assert.Equal("Purpose", option.GetAttribute("title"));
    }

    [Fact]
    public void SelectionFollowsFocus_ArrowDown_SelectsNextScenario()
    {
        var scenarios = new[] { Scenario("first", "First"), Scenario("second", "Second") };
        ScenarioDefinition? selected = null;

        var cut = Render<ScenarioBrowserPanel>(parameters => parameters
            .Add(panel => panel.Scenarios, scenarios)
            .Add(panel => panel.Selected, scenarios[0])
            .Add(panel => panel.ElevationReasonId, "reason")
            .Add(panel => panel.IsFavored, _ => false)
            .Add(panel => panel.IsScenarioDisabled, _ => false)
            .Add(panel => panel.OnSelect, scenario => selected = scenario));

        cut.Find(".scenario-browser__list").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        cut.WaitForAssertion(() => Assert.Equal("second", selected?.Id));
    }

    [Fact]
    public void SelectionStateless_SelectedParamDrivesDetail()
    {
        var scenarios = new[] { Scenario("first", "First"), Scenario("second", "Second") };

        var cut = Render<ScenarioBrowserPanel>(parameters => parameters
            .Add(panel => panel.Scenarios, scenarios)
            .Add(panel => panel.Selected, scenarios[1])
            .Add(panel => panel.ElevationReasonId, "reason")
            .Add(panel => panel.IsFavored, _ => false)
            .Add(panel => panel.IsScenarioDisabled, _ => false));

        Assert.Equal("Second", cut.Find(".scenario-detail__name").TextContent);

        cut.Render(parameters => parameters.Add(panel => panel.Selected, scenarios[0]));

        Assert.Equal("First", cut.Find(".scenario-detail__name").TextContent);
    }

    private static ScenarioDefinition Scenario(string id, string name, bool requiresAdmin = false) =>
        new()
        {
            Id = id,
            Name = name,
            Purpose = "Purpose",
            Group = ScenarioGroup.SystemHealth,
            Channels = ["System"],
            RequiresAdmin = requiresAdmin,
            Filters = []
        };
}
