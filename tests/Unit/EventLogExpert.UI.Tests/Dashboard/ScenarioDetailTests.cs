// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Filtering.Basic;
using EventLogExpert.Filtering.Common.Filtering;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Scenarios.Catalog;
using EventLogExpert.UI.Dashboard;

namespace EventLogExpert.UI.Tests.Dashboard;

public sealed class ScenarioDetailTests : BunitContext
{
    public ScenarioDetailTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    [Fact]
    public void AdminBadge_WhenNotRequiresAdmin_IsAbsent()
    {
        var cut = Render<ScenarioDetail>(parameters => parameters
            .Add(detail => detail.Scenario, Scenario("application-crashes", "Application crashes"))
            .Add(detail => detail.ElevationReasonId, "reason"));

        Assert.Empty(cut.FindAll(".scenario-detail__admin-badge"));
    }

    [Fact]
    public void AdminBadge_WhenRequiresAdmin_IsRendered()
    {
        var cut = Render<ScenarioDetail>(parameters => parameters
            .Add(detail => detail.Scenario, Scenario("application-crashes", "Application crashes") with
            {
                RequiresAdmin = true
            })
            .Add(detail => detail.ElevationReasonId, "reason"));

        var badge = cut.Find(".scenario-detail__admin-badge");

        Assert.Contains("Requires administrator", badge.TextContent);
        Assert.NotEmpty(cut.FindAll(".scenario-detail__admin-badge .bi-shield-lock"));
    }

    [Fact]
    public void Facts_ShowsRequiredChannels()
    {
        var cut = Render<ScenarioDetail>(parameters => parameters
            .Add(detail => detail.Scenario, Scenario("application-crashes", "Application crashes") with
            {
                Channels = ["Application", "System"]
            })
            .Add(detail => detail.ElevationReasonId, "reason"));

        var values = cut.FindAll(".scenario-detail__fact-value");

        Assert.Contains(values, value => value.TextContent == "Application, System");
    }

    [Fact]
    public void Facts_WhenNoOptionalChannels_OmitsAlsoIfPresentLine()
    {
        var cut = Render<ScenarioDetail>(parameters => parameters
            .Add(detail => detail.Scenario, Scenario("application-crashes", "Application crashes"))
            .Add(detail => detail.ElevationReasonId, "reason"));

        Assert.Empty(cut.FindAll(".scenario-detail__fact-muted"));
    }

    [Fact]
    public void Facts_WhenOptionalChannelsPresent_ShowsAlsoIfPresentLine()
    {
        var cut = Render<ScenarioDetail>(parameters => parameters
            .Add(detail => detail.Scenario, Scenario("application-crashes", "Application crashes") with
            {
                Channels = ["Application"],
                OptionalChannels = ["Setup", "Security"]
            })
            .Add(detail => detail.ElevationReasonId, "reason"));

        Assert.Equal(
            "Also if present: Setup, Security",
            cut.Find(".scenario-detail__fact-muted").TextContent);
    }

    [Fact]
    public void Filters_RenderOneFormattedLinePerRow()
    {
        var cut = Render<ScenarioDetail>(parameters => parameters
            .Add(detail => detail.Scenario, Scenario("application-crashes", "Application crashes") with
            {
                Filters =
                [
                    new ScenarioFilterRow(EqualsFilter(EventProperty.Id, "100")),
                    new ScenarioFilterRow(EqualsFilter(EventProperty.Level, "Error"))
                ]
            })
            .Add(detail => detail.ElevationReasonId, "reason"));

        var lines = cut.FindAll(".scenario-detail__filter");

        Assert.Equal(2, lines.Count);
        Assert.Equal("Id == 100", lines[0].TextContent.Trim());
        Assert.Equal("Level == \"Error\"", lines[1].TextContent.Trim());
    }

    [Fact]
    public void Filters_WhenColorSet_RendersSwatchWithDataHighlight()
    {
        var cut = Render<ScenarioDetail>(parameters => parameters
            .Add(detail => detail.Scenario, Scenario("application-crashes", "Application crashes") with
            {
                Filters = [new ScenarioFilterRow(EqualsFilter(EventProperty.Id, "100"), Color: HighlightColor.Red)]
            })
            .Add(detail => detail.ElevationReasonId, "reason"));

        Assert.Equal("red", cut.Find(".scenario-detail__swatch").GetAttribute("data-highlight"));
    }

    [Fact]
    public void Filters_WhenEmpty_OmitsFiltersSection()
    {
        var cut = Render<ScenarioDetail>(parameters => parameters
            .Add(detail => detail.Scenario, Scenario("application-crashes", "Application crashes"))
            .Add(detail => detail.ElevationReasonId, "reason"));

        Assert.Empty(cut.FindAll(".scenario-detail__filter"));
        Assert.DoesNotContain(
            cut.FindAll(".scenario-detail__fact-label"),
            label => label.TextContent == "Filters");
    }

    [Fact]
    public void Filters_WhenExcluded_PrefixesExclude()
    {
        var cut = Render<ScenarioDetail>(parameters => parameters
            .Add(detail => detail.Scenario, Scenario("application-crashes", "Application crashes") with
            {
                Filters = [new ScenarioFilterRow(EqualsFilter(EventProperty.Id, "100"), IsExcluded: true)]
            })
            .Add(detail => detail.ElevationReasonId, "reason"));

        Assert.Equal("Exclude Id == 100", cut.Find(".scenario-detail__filter").TextContent.Trim());
    }

    [Fact]
    public void Filters_WhenTryFormatFails_RowIsSkipped()
    {
        var cut = Render<ScenarioDetail>(parameters => parameters
            .Add(detail => detail.Scenario, Scenario("application-crashes", "Application crashes") with
            {
                Filters =
                [
                    new ScenarioFilterRow(EqualsFilter(EventProperty.Level, "   ")),
                    new ScenarioFilterRow(EqualsFilter(EventProperty.Id, "100"))
                ]
            })
            .Add(detail => detail.ElevationReasonId, "reason"));

        var lines = cut.FindAll(".scenario-detail__filter");

        Assert.Single(lines);
        Assert.Equal("Id == 100", lines[0].TextContent.Trim());
    }

    [Fact]
    public void Launch_WhenDisabled_IsGuardedAndDescribed()
    {
        bool launched = false;

        var cut = Render<ScenarioDetail>(parameters => parameters
            .Add(detail => detail.Scenario, Scenario("application-crashes", "Application crashes"))
            .Add(detail => detail.ElevationReasonId, "reason")
            .Add(detail => detail.IsDisabled, true)
            .Add(detail => detail.OnLaunch, () => launched = true));

        var launch = cut.Find(".scenario-detail__launch");

        Assert.Equal("true", launch.GetAttribute("aria-disabled"));
        Assert.Equal("reason", launch.GetAttribute("aria-describedby"));

        launch.Click();

        Assert.False(launched);
    }

    [Fact]
    public void Launch_WhenEnabled_InvokesLaunch()
    {
        bool launched = false;

        var cut = Render<ScenarioDetail>(parameters => parameters
            .Add(detail => detail.Scenario, Scenario("application-crashes", "Application crashes"))
            .Add(detail => detail.ElevationReasonId, "reason")
            .Add(detail => detail.OnLaunch, () => launched = true));

        cut.Find(".scenario-detail__launch").Click();

        Assert.True(launched);
    }

    [Fact]
    public void OpenFromFolder_Click_InvokesLaunchFromFolder()
    {
        bool launched = false;

        var cut = Render<ScenarioDetail>(parameters => parameters
            .Add(detail => detail.Scenario, Scenario("application-crashes", "Application crashes"))
            .Add(detail => detail.ElevationReasonId, "reason")
            .Add(detail => detail.OnLaunchFromFolder, () => launched = true));

        cut.Find(".scenario-detail__open-folder").Click();

        Assert.True(launched);
    }

    [Fact]
    public void OpenFromFolder_WhenLiveLaunchAdminDisabled_StaysAvailable()
    {
        bool launched = false;

        // Opening exported files from a folder needs no elevation, so the folder action ignores the live-launch admin gate.
        var cut = Render<ScenarioDetail>(parameters => parameters
            .Add(detail => detail.Scenario, Scenario("application-crashes", "Application crashes"))
            .Add(detail => detail.ElevationReasonId, "reason")
            .Add(detail => detail.IsDisabled, true)
            .Add(detail => detail.OnLaunchFromFolder, () => launched = true));

        var folder = cut.Find(".scenario-detail__open-folder");

        Assert.Null(folder.GetAttribute("aria-disabled"));

        folder.Click();

        Assert.True(launched);
    }

    [Fact]
    public void RendersNamePurposeAndEyebrow()
    {
        var cut = Render<ScenarioDetail>(parameters => parameters
            .Add(detail => detail.Scenario, Scenario("application-crashes", "Application crashes"))
            .Add(detail => detail.ElevationReasonId, "reason"));

        Assert.Equal("Application crashes", cut.Find(".scenario-detail__name").TextContent);
        Assert.Equal("Purpose", cut.Find(".scenario-detail__purpose").TextContent);
        Assert.Contains("System Health", cut.Find(".scenario-detail__eyebrow").TextContent);
    }

    [Fact]
    public void Star_Click_InvokesToggleFavorite()
    {
        bool toggled = false;

        var cut = Render<ScenarioDetail>(parameters => parameters
            .Add(detail => detail.Scenario, Scenario("application-crashes", "Application crashes"))
            .Add(detail => detail.ElevationReasonId, "reason")
            .Add(detail => detail.OnToggleFavorite, () => toggled = true));

        cut.Find(".scenario-detail__star").Click();

        Assert.True(toggled);
    }

    [Fact]
    public void Star_WhenFavored_IsPressedWithFilledIcon()
    {
        var cut = Render<ScenarioDetail>(parameters => parameters
            .Add(detail => detail.Scenario, Scenario("application-crashes", "Application crashes"))
            .Add(detail => detail.ElevationReasonId, "reason")
            .Add(detail => detail.IsFavored, true));

        Assert.Equal("true", cut.Find(".scenario-detail__star").GetAttribute("aria-pressed"));
        Assert.NotEmpty(cut.FindAll(".scenario-detail__star .bi-star-fill"));
    }

    private static BasicFilter EqualsFilter(EventProperty property, string value) =>
        new(
            new FilterComparison
            {
                Property = property,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = value
            },
            []);

    private static ScenarioDefinition Scenario(string id, string name) =>
        new()
        {
            Id = id,
            Name = name,
            Purpose = "Purpose",
            Group = ScenarioGroup.SystemHealth,
            Channels = ["System"],
            Filters = []
        };
}
