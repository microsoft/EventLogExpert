// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.UI.DatabaseTools.Tabs;

namespace EventLogExpert.UI.Tests.DatabaseTools.Tabs;

public sealed class ManageDatabasesEmptyStateTests : BunitContext
{
    [Fact]
    public void ManageDatabasesEmptyState_BoldsImportButtonReferenceForVisualHierarchy()
    {
        var component = Render<ManageDatabasesEmptyState>();

        var strong = component.Find(".manage-databases-empty strong");
        Assert.Equal("Import database\u2026", strong.TextContent);
    }

    [Fact]
    public void ManageDatabasesEmptyState_HasNoRoleAttribute_StaticContentNotLiveRegion()
    {
        var component = Render<ManageDatabasesEmptyState>();

        var empty = component.Find(".manage-databases-empty");
        Assert.False(
            empty.HasAttribute("role"),
            "static empty state should not declare role=status; live-region semantics are reserved for dynamic announcements");
    }

    [Fact]
    public void ManageDatabasesEmptyState_RendersInstructionsReferencingImportButton()
    {
        var component = Render<ManageDatabasesEmptyState>();

        var empty = component.Find(".manage-databases-empty");
        Assert.Contains("Import a provider database", empty.TextContent);
        Assert.Contains("Import database\u2026", empty.TextContent);
    }
}
