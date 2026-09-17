// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Localization;
using EventLogExpert.UI.DatabaseTools.Tabs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.Tests.DatabaseTools.Tabs;

public sealed class ManageDatabasesEmptyStateTests : BunitContext
{
    public ManageDatabasesEmptyStateTests() =>
        Services.AddSingleton<IStringLocalizer<SharedResource>>(new EmptyStateLocalizer());

    [Fact]
    public void ManageDatabasesEmptyState_BoldsImportButtonReferenceForVisualHierarchy()
    {
        var component = Render<ManageDatabasesEmptyState>();

        var strong = component.Find(".manage-databases-empty strong");
        Assert.Equal("Import database…", strong.TextContent);
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
        Assert.Contains("Import database…", empty.TextContent);
    }

    private sealed class EmptyStateLocalizer : IStringLocalizer<SharedResource>
    {
        public LocalizedString this[string name] => new(
            name,
            "Import a provider database (.db file) using the <strong>Import database…</strong> button above to resolve events from other providers.",
            resourceNotFound: false);

        public LocalizedString this[string name, params object[] arguments] => this[name];

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
