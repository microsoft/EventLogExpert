// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Localization;
using EventLogExpert.Scenarios.Catalog;
using EventLogExpert.UI.Common;
using EventLogExpert.UI.Dashboard;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

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

    [Theory]
    [InlineData("Dashboard_Presence_Present", "Present")]
    [InlineData("Dashboard_Presence_Absent", "Not present")]
    [InlineData("Dashboard_Presence_Unknown", "Presence unknown")]
    [InlineData("Dashboard_Enablement_Enabled", "Enabled")]
    [InlineData("Dashboard_Enablement_Disabled", "Disabled")]
    [InlineData("Dashboard_Enablement_Unknown", "Enablement unknown")]
    public void EnumLabelKeys_ResolveToEnglish(string key, string expected)
    {
        var localized = Localizer[key];

        Assert.False(localized.ResourceNotFound, $"Missing RESX key {key}");
        Assert.Equal(expected, localized.Value);
    }

    [Fact]
    public void EveryDashboardResourceKey_IsReferencedInSource()
    {
        var (resxPath, uiSourceRoot) = LocateUiSource();

        // No-op when the source tree is not alongside the test binary (e.g. a packaged run).
        if (resxPath is null || uiSourceRoot is null) { return; }

        var keys = XDocument.Load(resxPath)
            .Root!.Elements("data")
            .Select(data => (string?)data.Attribute("name"))
            .Where(name => name is not null && name.StartsWith("Dashboard_", StringComparison.Ordinal))
            .Select(name => name!)
            .ToList();

        var source = string.Join(
            "\n",
            Directory.EnumerateFiles(uiSourceRoot, "*.cs", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(uiSourceRoot, "*.razor", SearchOption.AllDirectories))
                .Select(File.ReadAllText));

        // Dashboard_Group_* are composed via $"Dashboard_Group_{group}" (and pinned by the drift-guard test above);
        // every other Dashboard_* key must appear as a literal "key" reference in the component sources. This catches the
        // authored-but-never-referenced failure the one-directional DoesNotContain("Dashboard_") guard cannot.
        var orphans = keys
            .Where(key => !key.StartsWith("Dashboard_Group_", StringComparison.Ordinal))
            .Where(key => !source.Contains($"\"{key}\"", StringComparison.Ordinal))
            .ToList();

        Assert.True(orphans.Count == 0, $"Authored-but-unreferenced Dashboard_* RESX keys: {string.Join(", ", orphans)}");
    }

    [Fact]
    public void GroupDisplay_ForEveryScenarioGroup_ResolvesAndEqualsDisplayName()
    {
        var localizer = Localizer;

        foreach (var group in Enum.GetValues<ScenarioGroup>())
        {
            var localized = localizer[$"Dashboard_Group_{group}"];

            Assert.False(localized.ResourceNotFound, $"Missing RESX key Dashboard_Group_{group}");
            Assert.Equal(group.DisplayName(), localized.Value);
            Assert.Equal(ScenarioGroupLocalizer.GroupDisplay(localizer, group), localized.Value);
        }
    }

    [Theory]
    [InlineData("Dashboard_File_One", "Dashboard_File_Many", "1 file", "2 files")]
    [InlineData("Dashboard_Log_One", "Dashboard_Log_Many", "1 log", "2 logs")]
    [InlineData("Dashboard_Channel_One", "Dashboard_Channel_Many", "1 channel", "2 channels")]
    public void PluralKeys_AreCountInclusive(string oneKey, string manyKey, string singular, string plural)
    {
        var localizer = Localizer;

        Assert.Equal(singular, localizer[oneKey].Value);
        Assert.Equal(plural, localizer[manyKey, 2].Value);
    }

    [Fact]
    public void ScenarioBrowserPanel_EmptyState_IsLocalized_AndDoesNotLeakResourceKeys()
    {
        var cut = Render<ScenarioBrowserPanel>(parameters => parameters
            .Add(panel => panel.Scenarios, Array.Empty<ScenarioDefinition>()));

        var markup = cut.Markup;

        Assert.Contains("No scenarios available in this category.", markup);
        Assert.DoesNotContain("Dashboard_", markup);
    }

    [Fact]
    public void ScenarioBrowserPanel_ListAria_IsLocalized()
    {
        var cut = Render<ScenarioBrowserPanel>(parameters => parameters
            .Add(panel => panel.Scenarios, [Scenario()])
            .Add(panel => panel.IsFavored, _ => false)
            .Add(panel => panel.IsScenarioDisabled, _ => false));

        Assert.Equal("Scenarios", cut.Find("ul[role='listbox']").GetAttribute("aria-label"));
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

        // The favorite aria composes the (data) scenario name into a localized "Add {0} to favorites" format.
        Assert.Contains("Add Zzz Sentinel Scenario to favorites", markup);
    }

    [Fact]
    public void ScenarioDetail_RendersLocalizedLabels_AndDoesNotLeakResourceKeys()
    {
        var cut = Render<ScenarioDetail>(parameters => parameters
            .Add(detail => detail.Scenario, Scenario()));

        var markup = cut.Markup;

        Assert.Contains("System Health", markup); // ScenarioGroup.SystemHealth eyebrow
        Assert.Contains("Logs", markup);
        Assert.Contains("Launch", markup);
        Assert.Contains("Open from folder", markup);
        Assert.Contains("Include subfolders", markup);
        Assert.DoesNotContain("Dashboard_", markup);
    }

    private static (string? ResxPath, string? UiSourceRoot) LocateUiSource([CallerFilePath] string testFilePath = "")
    {
        var directory = Path.GetDirectoryName(testFilePath);

        while (directory is not null && !Directory.Exists(Path.Combine(directory, "src", "EventLogExpert.UI")))
        {
            directory = Path.GetDirectoryName(directory);
        }

        if (directory is null) { return (null, null); }

        var uiRoot = Path.Combine(directory, "src", "EventLogExpert.UI");
        var resx = Path.Combine(directory, "src", "EventLogExpert.Localization", "Resources", "SharedResource.resx");

        return (File.Exists(resx) ? resx : null, Directory.Exists(uiRoot) ? uiRoot : null);
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
