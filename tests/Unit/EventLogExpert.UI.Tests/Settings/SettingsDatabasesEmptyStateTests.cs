// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.UI.Settings;

namespace EventLogExpert.UI.Tests.Settings;

public sealed class SettingsDatabasesEmptyStateTests : BunitContext
{
    [Fact]
    public void SettingsDatabasesEmptyState_BoldsImportButtonReferenceForVisualHierarchy()
    {
        var component = Render<SettingsDatabasesEmptyState>();

        var strong = component.Find(".settings-databases-empty strong");
        Assert.Equal("Import Database", strong.TextContent);
    }

    [Fact]
    public void SettingsDatabasesEmptyState_HasNoRoleAttribute_StaticContentNotLiveRegion()
    {
        var component = Render<SettingsDatabasesEmptyState>();

        var empty = component.Find(".settings-databases-empty");
        Assert.False(
            empty.HasAttribute("role"),
            "static empty state should not declare role=status; live-region semantics are reserved for dynamic announcements");
    }

    [Fact]
    public void SettingsDatabasesEmptyState_RendersInstructionsReferencingImportButton()
    {
        var component = Render<SettingsDatabasesEmptyState>();

        var empty = component.Find(".settings-databases-empty");
        Assert.Contains("Import a provider database", empty.TextContent);
        Assert.Contains("Import Database", empty.TextContent);
    }
}
