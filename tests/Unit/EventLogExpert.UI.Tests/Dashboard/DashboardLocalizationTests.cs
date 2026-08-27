// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Localization;
using EventLogExpert.Scenarios.Catalog;
using EventLogExpert.UI.Common;
using EventLogExpert.UI.Dashboard;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.Tests.Dashboard;

/// <summary>
///     Localization coverage for the Dashboard surface: the group-name drift guard, count-inclusive plurals, the
///     localized-label render + resource-key-leak guards, and the data-vs-UI boundary (catalog data renders verbatim). The
///     exclude-filter prefix (localized) with verbatim formatter text is covered by
///     <see cref="ScenarioDetailTests.Filters_WhenExcluded_PrefixesExclude" />, and the diagnostic <c>result.Message</c>
///     pass-through by <see cref="EmptyStateDashboardTests.DetailOpenFromFolder_WhenError_ShowsErrorAlert" />.
/// </summary>
public sealed class DashboardLocalizationTests : BunitContext
{
    public DashboardLocalizationTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddEventLogLocalization();
    }

    private IStringLocalizer<SharedResource> Localizer =>
        Services.GetRequiredService<IStringLocalizer<SharedResource>>();

    [Fact]
    public void GroupDisplay_ForEveryScenarioGroup_ResolvesAndEqualsDisplayName()
    {
        var localizer = Localizer;

        // Cross-source drift guard: FilterPane renders ScenarioGroup.DisplayName() unlocalized while Dashboard renders Dashboard_Group_*; editing a Dashboard_Group_* value is a 3-place co-edit with ScenarioGroupDisplay.cs and ScenarioGroupDisplayTests.cs.
        foreach (var group in Enum.GetValues<ScenarioGroup>())
        {
            var localized = localizer[$"Dashboard_Group_{group}"];

            Assert.False(localized.ResourceNotFound, $"Missing RESX key Dashboard_Group_{group}");
            Assert.Equal(group.DisplayName(), localized.Value);
            Assert.Equal(ScenarioGroupLocalizer.GroupDisplay(localizer, group), localized.Value);
        }
    }

    [Theory]
    [InlineData("Dashboard_File_One", "Dashboard_File_Many")]
    [InlineData("Dashboard_Log_One", "Dashboard_Log_Many")]
    [InlineData("Dashboard_Channel_One", "Dashboard_Channel_Many")]
    public void PluralKeys_AreCountInclusive(string oneKey, string manyKey)
    {
        var localizer = Localizer;

        Assert.Contains("1", localizer[oneKey, 1].Value);
        Assert.Contains("2", localizer[manyKey, 2].Value);
        Assert.NotEqual(localizer[manyKey].Value, localizer[manyKey, 2].Value);
    }

    [Fact]
    public void ScenarioBrowserPanel_EmptyState_IsLocalized_AndDoesNotLeakResourceKeys()
    {
        var cut = Render<ScenarioBrowserPanel>(parameters => parameters
            .Add(panel => panel.Scenarios, Array.Empty<ScenarioDefinition>()));

        var markup = cut.Markup;

        Assert.DoesNotContain("Dashboard_", markup);
    }

    [Fact]
    public void ScenarioDetail_LeavesCatalogDataVerbatim()
    {
        var scenario = Scenario() with
        {
            Name = "Zzz Sentinel Scenario",
            Purpose = "Zzz Sentinel Purpose",
            Channels = ["Zzz-Sentinel-Channel"]
        };

        var cut = Render<ScenarioDetail>(parameters => parameters
            .Add(detail => detail.Scenario, scenario));

        var markup = cut.Markup;

        Assert.Contains("Zzz Sentinel Scenario", markup);
        Assert.Contains("Zzz Sentinel Purpose", markup);
        Assert.Contains("Zzz-Sentinel-Channel", markup);
    }

    [Fact]
    public void ScenarioDetail_RendersLocalizedLabels_AndDoesNotLeakResourceKeys()
    {
        var cut = Render<ScenarioDetail>(parameters => parameters
            .Add(detail => detail.Scenario, Scenario()));

        var markup = cut.Markup;

        Assert.Contains(Localizer["Dashboard_Group_SystemHealth"].Value, markup);
        Assert.DoesNotContain("Dashboard_", markup);
    }

    private static ScenarioDefinition Scenario() =>
        new()
        {
            Id = "application-crashes",
            Name = "Application crashes",
            Purpose = "Purpose",
            Group = ScenarioGroup.SystemHealth,
            Channels = ["System"],
            Filters = []
        };
}
