// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using AngleSharp.Dom;
using Bunit;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Localization;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.Scenarios;
using EventLogExpert.UI.FilterLibrary;
using EventLogExpert.UI.Menu;
using EventLogExpert.UI.Modal;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Tests.FilterLibrary;

public sealed class FilterLibraryChromeLocalizerWiringTests : BunitContext
{
    private readonly IAlertDialogService _alerts = Substitute.For<IAlertDialogService>();
    private readonly IAnnouncementService _announcements = Substitute.For<IAnnouncementService>();
    private readonly IFilterLibraryCommands _commands = Substitute.For<IFilterLibraryCommands>();
    private readonly IMenuService _menuService = Substitute.For<IMenuService>();
    private readonly IModalService _modalService = Substitute.For<IModalService>();
    private ImmutableList<LibraryEntry> _entries = [];
    private ImmutableList<SavedFilter> _filters = [];

    public FilterLibraryChromeLocalizerWiringTests()
    {
        Services.AddBannerHostDependencies();
        Services.AddSingleton(_alerts);
        Services.AddSingleton(_announcements);
        Services.AddSingleton(_commands);
        Services.AddSingleton(_modalService);
        Services.AddSingleton(_menuService);
        Services.AddSingleton(Substitute.For<IMenuHostRegistry>());
        Services.AddSingleton(Substitute.For<IFilePickerService>());
        Services.AddSingleton(Substitute.For<IFilterLibraryExportService>());
        Services.AddSingleton(Substitute.For<IClipboardService>());
        Services.AddSingleton(Substitute.For<IScenarioAuthoringService>());
        Services.AddSingleton(Substitute.For<ITagBulkUpdateFailedNotifier>());
        Services.AddSingleton(new ScenarioAuthoringOptions(false));
        Services.AddSingleton<IStringLocalizer<SharedResource>>(new MarkerLocalizer());

        IActiveFiltersSource activeFilters = Substitute.For<IActiveFiltersSource>();
        activeFilters.Current.Returns(_ => _filters);
        Services.AddSingleton(activeFilters);

        ILibraryEntriesSource libraryEntries = Substitute.For<ILibraryEntriesSource>();
        libraryEntries.Current.Returns(_ => _entries);
        Services.AddSingleton(libraryEntries);

        ILibraryLoadStatusSource loadStatus = Substitute.For<ILibraryLoadStatusSource>();
        loadStatus.Current.Returns(new LibraryLoadStatus(IsLoaded: true, LoadError: false));
        Services.AddSingleton(loadStatus);

        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./_content/EventLogExpert.UI/Menu/MenuAnchor.js")
            .Setup<MenuAnchorRect>("getMenuElementRect", _ => true)
            .SetResult(new MenuAnchorRect(0, 0, 0, 0, 0, 0));
    }

    [Fact]
    public async Task EditorWrapper_RoutesEmptyStateAndAddFilterActionThroughLocalizer()
    {
        LibraryEntryFilterSet filterSet = BuildFilterSet("Set", filterCount: 0);
        IRenderedComponent<LibraryEntryFilterEditor> component = Render<LibraryEntryFilterEditor>(parameters => parameters
            .Add(editor => editor.FilterSet, filterSet)
            .Add(editor => editor.IsExpanded, true));

        Assert.Equal("[[FilterLibrary_Editor_Empty]]", component.Find(".library-entry-filter-editor-empty").TextContent);
        Assert.Contains("[[FilterLibrary_Editor_AddFilter]]", component.Find(".library-entry-filter-editor-add-button").TextContent);

        await component.Find(".library-entry-filter-editor-add-button").ClickAsync(new MouseEventArgs());

        Assert.Single(component.FindComponents<LibraryFilterRow>());
    }

    [Fact]
    public void ModalManageTagsToggle_RoutesAriaAndTitleThroughDistinctKeys()
    {
        IRenderedComponent<FilterLibraryModal> component = RenderModalWithTagCount(1);
        IElement trigger = component.Find(".library-tag-management-trigger");

        Assert.Equal("[[FilterLibrary_ManageTagsAria]]", trigger.GetAttribute("aria-label"));
        Assert.Equal("[[FilterLibrary_ManageTagsTitle]]", trigger.GetAttribute("title"));
    }

    [Fact]
    public async Task ModalManageTagsToggle_WhenExpanded_RoutesHideTagManagementAria()
    {
        IRenderedComponent<FilterLibraryModal> component = RenderModalWithTagCount(1);

        await component.Find(".library-tag-management-trigger").ClickAsync(new MouseEventArgs());

        IElement trigger = component.Find(".library-tag-management-trigger");
        Assert.Equal("[[FilterLibrary_HideTagManagementAria]]", trigger.GetAttribute("aria-label"));
        Assert.Equal("[[FilterLibrary_ManageTagsTitle]]", trigger.GetAttribute("title"));
    }

    [Fact]
    public void ModalTagOverflow_RoutesSingularAndPluralAdditionalTagsAriaThroughDistinctKeys()
    {
        IRenderedComponent<FilterLibraryModal> singular = RenderModalWithTagCount(11);
        IElement singularOverflow = singular.Find(".library-tag-filter-chip-overflow");

        Assert.Equal("[[FilterLibrary_ShowAdditionalTagsAria_One(1)]]", singularOverflow.GetAttribute("aria-label"));
        Assert.Equal("[[FilterLibrary_ShowMore(1)]]", singularOverflow.TextContent.Trim());

        IRenderedComponent<FilterLibraryModal> plural = RenderModalWithTagCount(12);
        IElement pluralOverflow = plural.Find(".library-tag-filter-chip-overflow");

        Assert.Equal("[[FilterLibrary_ShowAdditionalTagsAria_Many(2)]]", pluralOverflow.GetAttribute("aria-label"));
        Assert.Equal("[[FilterLibrary_ShowMore(2)]]", pluralOverflow.TextContent.Trim());
    }

    [Fact]
    public async Task ModalTagOverflow_WhenExpanded_RoutesHideAriaAndShowLessTextThroughLocalizer()
    {
        IRenderedComponent<FilterLibraryModal> component = RenderModalWithTagCount(12);
        IElement overflow = component.Find(".library-tag-filter-chip-overflow");

        await overflow.ClickAsync(new MouseEventArgs());

        IElement expandedOverflow = component.Find(".library-tag-filter-chip-overflow");
        Assert.Equal("[[FilterLibrary_HideAdditionalTagsAria]]", expandedOverflow.GetAttribute("aria-label"));
        Assert.Equal("[[FilterLibrary_ShowLess]]", expandedOverflow.TextContent.Trim());
    }

    [Fact]
    public async Task RowChrome_RoutesLabelsMenusDialogsAndAnnouncementsThroughLocalizer()
    {
        LibraryEntrySavedFilter entry = BuildSavedFilter("Entry") with { Tags = ["alpha", "beta", "gamma"] };
        LibraryEntryFilterSet filterSet = BuildFilterSet("Set", filterCount: 2);
        _entries = [entry, filterSet];
        _filters = [SavedFilter.TryCreate("Level == 9")!];
        _alerts.ShowAlert(default!, default!, default!, default(string)!).ReturnsForAnyArgs(false);
        IRenderedComponent<LibraryEntryRow> component = RenderRow(entry, allFilterSets: [filterSet]);

        Assert.Equal("[[FilterLibrary_Entry_FilterKindAria]]", component.Find("i.library-entry-kind-icon").GetAttribute("aria-label"));
        Assert.Equal("[[FilterLibrary_Entry_RemoveTagAria(alpha)]]", component.Find(".library-entry-tag-chip-remove").GetAttribute("aria-label"));
        Assert.Equal("[[FilterLibrary_Entry_ShowAllTagsAria(3)]]", component.Find(".library-entry-tag-chip-more").GetAttribute("aria-label"));
        Assert.Contains("[[FilterLibrary_Entry_MoreTags(1)]]", component.Markup);
        Assert.Equal("[[FilterLibrary_Entry_EditTagsAria(Entry)]]", component.Find(".library-entry-tag-add-inline").GetAttribute("aria-label"));
        Assert.Equal("[[FilterLibrary_Entry_ApplyAria(Entry)]]", component.Find("button.button-green").GetAttribute("aria-label"));
        Assert.Contains("[[Modal_Apply]]", component.Find("button.button-green").TextContent);

        IReadOnlyList<MenuItem> items = await CapturedMoreMenuItemsAsync(component);
        Assert.Contains(items, item => item.Label == "[[FilterLibrary_Entry_ReplaceCurrentMenu]]");
        Assert.Contains(items, item => item.Label == "[[FilterLibrary_Entry_AddToFilterSetMenu]]");
        Assert.Contains(items, item => item.Label == "[[FilterLibrary_Entry_RenameMenu]]");
        Assert.Contains(items, item => item.Label == "[[FilterLibrary_Entry_DeleteMenu]]");

        await items.Single(item => item.Label == "[[FilterLibrary_Entry_ReplaceCurrentMenu]]").OnClickAsync!.Invoke();

        await _alerts.Received(1).ShowAlert(
            "[[FilterLibrary_Entry_ReplacePromptTitle]]",
            "[[FilterLibrary_Entry_ReplacePromptMessage(Entry)]]",
            "[[FilterPane_Action_Replace]]",
            "[[Modal_Cancel]]");

        await component.Find(".library-entry-tag-chip-remove").ClickAsync(new MouseEventArgs());

        _announcements.Received(1).Announce("[[FilterLibrary_Entry_TagRemovedAnnouncement(alpha|Entry)]]");
    }

    [Fact]
    public async Task SavedTabHeader_RoutesDraftChromeValidationAndAnnouncementThroughLocalizer()
    {
        LibraryEntrySavedFilter existing = BuildSavedFilter("Existing");
        IRenderedComponent<LibrarySavedTabHeader> component = Render<LibrarySavedTabHeader>(parameters => parameters
            .Add(header => header.AllLibraryTags, new[] { "tag" })
            .Add(header => header.ExistingSavedFilters, new[] { existing }));

        Assert.Contains("[[FilterLibrary_Entry_NewSavedFilterAction]]", component.Find(".library-saved-tab-new-button").TextContent);
        await component.Find(".library-saved-tab-new-button").ClickAsync(new MouseEventArgs());

        Assert.Equal("[[FilterLibrary_Entry_NewSavedFilterDraftAria]]", component.Find(".library-saved-tab-new-draft").GetAttribute("aria-label"));
        Assert.Equal("[[FilterLibrary_Entry_NameLabel]]", component.Find("label").TextContent);
        Assert.Equal("[[FilterLibrary_Entry_NamePlaceholder]]", component.Find("input").GetAttribute("placeholder"));
        Assert.Contains("[[FilterLibrary_Entry_NewSavedFilterTagsAria]]", component.Markup);

        await component.Find("input").InputAsync(new ChangeEventArgs { Value = "Existing" });

        Assert.Equal("[[FilterLibrary_Entry_SavedFilterExistsValidation(Existing)]]", component.Find(".library-saved-tab-new-draft-error").TextContent);
    }

    [Fact]
    public void TagPanelDeleteAction_RoutesAriaAndTitleThroughDistinctKeys()
    {
        LibraryEntrySavedFilter entry = BuildSavedFilter("Entry") with { Tags = ["bug"] };
        IRenderedComponent<TagManagementPanel> component = Render<TagManagementPanel>(parameters => parameters
            .Add(panel => panel.AllLibraryTags, new[] { "bug" })
            .Add(panel => panel.AllEntries, new[] { entry }));
        IElement deleteButton = component.FindAll(".library-tag-management-row button")
            .Single(button => button.GetAttribute("title") == "[[FilterLibrary_Tags_DeleteTitle]]");

        Assert.Equal("[[FilterLibrary_Tags_DeleteActionAria(bug)]]", deleteButton.GetAttribute("aria-label"));
        Assert.Equal("[[FilterLibrary_Tags_DeleteTitle]]", deleteButton.GetAttribute("title"));
    }

    [Theory]
    [InlineData(1, "[[FilterLibrary_Tags_RemoveConfirm_One(1)]]")]
    [InlineData(2, "[[FilterLibrary_Tags_RemoveConfirm_Many(2)]]")]
    public async Task TagPanelDelete_RoutesOneAndManyAffectedCountsThroughCorrectKeys(int count, string expectedConfirm)
    {
        IReadOnlyList<LibraryEntry> entries = Enumerable.Range(0, count)
            .Select(index => BuildSavedFilter($"Entry {index}") with { Tags = ["source"] })
            .ToArray();
        IRenderedComponent<TagManagementPanel> component = Render<TagManagementPanel>(parameters => parameters
            .Add(panel => panel.AllLibraryTags, new[] { "source" })
            .Add(panel => panel.AllEntries, entries));

        await component.Find(".library-tag-management-row .button-red").ClickAsync(new MouseEventArgs());

        Assert.Equal(expectedConfirm, component.Find(".library-tag-management-row-confirm").TextContent);
        Assert.Equal("[[FilterLibrary_Tags_ConfirmDeleteAria(source)]]", component.Find(".library-tag-management-row .button-red").GetAttribute("aria-label"));
        Assert.Equal(
            "[[FilterLibrary_Tags_CancelDeleteAria]]",
            component.FindAll(".library-tag-management-row button")
                .Single(button => button.GetAttribute("aria-label") == "[[FilterLibrary_Tags_CancelDeleteAria]]")
                .GetAttribute("aria-label"));
    }

    [Theory]
    [InlineData(1, "[[FilterLibrary_Tags_MergeAria_One(source|target|1)]]")]
    [InlineData(2, "[[FilterLibrary_Tags_MergeAria_Many(source|target|2)]]")]
    public async Task TagPanelMerge_RoutesOneAndManyAffectedCountsThroughCorrectKeys(int count, string expectedAria)
    {
        IReadOnlyList<LibraryEntry> entries = Enumerable.Range(0, count)
            .Select(index => BuildSavedFilter($"Entry {index}") with { Tags = ["source"] })
            .Append(BuildSavedFilter("Target") with { Tags = ["target"] })
            .ToArray();
        IRenderedComponent<TagManagementPanel> component = Render<TagManagementPanel>(parameters => parameters
            .Add(panel => panel.AllLibraryTags, new[] { "source", "target" })
            .Add(panel => panel.AllEntries, entries));

        await component.Find(".library-tag-management-row .button").ClickAsync(new MouseEventArgs());
        await component.Find(".library-tag-management-row-edit-input").InputAsync(new ChangeEventArgs { Value = "target" });
        await component.Find(".library-tag-management-row .button-green").ClickAsync(new MouseEventArgs());

        Assert.Equal(expectedAria, component.Find(".library-tag-management-row .button-yellow").GetAttribute("aria-label"));
        Assert.Contains("[[FilterLibrary_Tags_MergeAction(target)]]", component.Find(".library-tag-management-row .button-yellow").TextContent);
    }

    [Fact]
    public async Task TagPanelRenameControls_RouteInputAndActionAriaThroughDistinctKeys()
    {
        LibraryEntrySavedFilter entry = BuildSavedFilter("Entry") with { Tags = ["bug"] };
        IRenderedComponent<TagManagementPanel> component = Render<TagManagementPanel>(parameters => parameters
            .Add(panel => panel.AllLibraryTags, new[] { "bug" })
            .Add(panel => panel.AllEntries, new[] { entry }));
        IElement renameButton = component.FindAll(".library-tag-management-row button")
            .Single(button => button.GetAttribute("title") == "[[FilterLibrary_Tags_RenameTitle]]");

        Assert.Equal("[[FilterLibrary_Tags_RenameActionAria(bug)]]", renameButton.GetAttribute("aria-label"));
        Assert.Equal("[[FilterLibrary_Tags_RenameTitle]]", renameButton.GetAttribute("title"));

        await renameButton.ClickAsync(new MouseEventArgs());

        Assert.Equal(
            "[[FilterLibrary_Tags_RenameInputAria(bug)]]",
            component.Find(".library-tag-management-row-edit-input").GetAttribute("aria-label"));
    }

    private static LibraryEntryFilterSet BuildFilterSet(string name, int filterCount) =>
        new()
        {
            Name = name,
            CreatedUtc = DateTimeOffset.UtcNow,
            Filters = [.. Enumerable.Range(0, filterCount).Select(index => SavedFilter.TryCreate($"Level == {index}")!)],
            Origin = LibraryEntryOrigin.UserSaved,
        };

    private static LibraryEntrySavedFilter BuildSavedFilter(string name) =>
        new()
        {
            Name = name,
            CreatedUtc = DateTimeOffset.UtcNow,
            Filter = SavedFilter.TryCreate("Level == 4")!,
            Origin = LibraryEntryOrigin.UserSaved,
        };

    private async Task<IReadOnlyList<MenuItem>> CapturedMoreMenuItemsAsync(IRenderedComponent<LibraryEntryRow> component)
    {
        IReadOnlyList<MenuItem>? captured = null;
        _menuService.WhenForAnyArgs(service => service.OpenAt(0, 0, null!, false, false))
            .Do(call => captured = call.ArgAt<IReadOnlyList<MenuItem>>(2));

        await component.Find(".library-entry-row-main button[aria-haspopup='menu']").ClickAsync(new MouseEventArgs { Detail = 1 });

        Assert.NotNull(captured);
        return captured;
    }

    private IRenderedComponent<FilterLibraryModal> RenderModalWithTagCount(int tagCount)
    {
        _entries = [.. Enumerable.Range(1, tagCount).Select(index => BuildSavedFilter($"Entry {index}") with { Tags = [$"tag{index:00}"] })];

        return Render<FilterLibraryModal>();
    }

    private IRenderedComponent<LibraryEntryRow> RenderRow(
        LibraryEntry entry,
        IReadOnlyList<LibraryEntryFilterSet> allFilterSets) =>
        Render<LibraryEntryRow>(parameters => parameters
            .Add(row => row.ActiveTab, LibraryTab.Saved)
            .Add(row => row.Entry, entry)
            .Add(row => row.AllFilterSets, allFilterSets)
            .Add(row => row.OnAddToFilterSet, _ => Task.CompletedTask)
            .Add(row => row.OnApply, _ => Task.CompletedTask)
            .Add(row => row.OnDelete, _ => Task.CompletedTask)
            .Add(row => row.OnReplace, _ => Task.CompletedTask)
            .Add(row => row.OnRequestPendingFocus, _ => Task.CompletedTask)
            .Add(row => row.OnSaveToLibrary, _ => Task.CompletedTask)
            .Add(row => row.OnToggleFavorite, _ => Task.CompletedTask));
}
