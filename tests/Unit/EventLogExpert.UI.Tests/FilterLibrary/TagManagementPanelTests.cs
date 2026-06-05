// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.UI.FilterLibrary;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventLogExpert.UI.Tests.FilterLibrary;

public sealed class TagManagementPanelTests : BunitContext
{
    private readonly IAnnouncementService _announcements = Substitute.For<IAnnouncementService>();
    private readonly IFilterLibraryCommands _commands = Substitute.For<IFilterLibraryCommands>();

    public TagManagementPanelTests()
    {
        Services.AddSingleton(_announcements);
        Services.AddSingleton(_commands);

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void RendersOnePerTag_WithUsageCount()
    {
        var a = BuildEntry("a", ["bug", "perf"]);
        var b = BuildEntry("b", ["bug"]);
        var component = Render(allTags: ["bug", "perf"], entries: [a, b]);

        var rows = component.FindAll(".library-tag-management-row");
        Assert.Equal(2, rows.Count);
        Assert.Contains("bug", rows[0].TextContent);
        Assert.Contains("(2)", rows[0].TextContent);
        Assert.Contains("perf", rows[1].TextContent);
        Assert.Contains("(1)", rows[1].TextContent);
    }

    [Fact]
    public void SearchBox_HiddenWhenFewerThan6Tags()
    {
        var entry = BuildEntry("a", ["bug"]);
        var component = Render(allTags: ["bug"], entries: [entry]);

        Assert.Empty(component.FindAll(".library-tag-management-search"));
    }

    [Fact]
    public void SearchBox_VisibleWhenAtLeast6Tags()
    {
        var tags = Enumerable.Range(1, 6).Select(i => $"tag{i}").ToList();
        var entry = BuildEntry("a", [.. tags]);
        var component = Render(allTags: tags, entries: [entry]);

        Assert.NotNull(component.Find(".library-tag-management-search"));
    }

    [Fact]
    public async Task SearchBox_FiltersTagListInPlace_CaseInsensitive()
    {
        var tags = Enumerable.Range(1, 6).Select(i => $"tag{i}").ToList();
        tags.Add("bug");
        var entry = BuildEntry("a", [.. tags]);
        var component = Render(allTags: tags, entries: [entry]);

        var search = component.Find(".library-tag-management-search input");
        await search.InputAsync(new ChangeEventArgs { Value = "BUG" });

        var rows = component.FindAll(".library-tag-management-row");
        Assert.Single(rows);
        Assert.Contains("bug", rows[0].TextContent);
    }

    [Fact]
    public async Task ConfirmRename_NoCollision_DispatchesRenameTagCommand_AndCallback()
    {
        var entry = BuildEntry("a", ["bug"]);
        (string OldName, string NewName)? captured = null;
        var component = Render(
            allTags: ["bug"],
            entries: [entry],
            onRenamed: t => { captured = t; return Task.CompletedTask; });

        await BeginRenameAsync(component);
        await SetEditInputAsync(component, "defect");
        await component.Find(".library-tag-management-row .button-green").ClickAsync(new MouseEventArgs());

        _commands.Received(1).RenameTag("bug", "defect");
        Assert.NotNull(captured);
        Assert.Equal(("bug", "defect"), captured!.Value);
    }

    [Fact]
    public async Task ConfirmRename_NameCollision_ShowsMergeButton_DoesNotDispatch()
    {
        var entry = BuildEntry("a", ["bug", "defect"]);
        var component = Render(allTags: ["bug", "defect"], entries: [entry]);

        await BeginRenameAsync(component);
        await SetEditInputAsync(component, "defect");
        await component.Find(".library-tag-management-row .button-green").ClickAsync(new MouseEventArgs());

        Assert.NotNull(component.Find(".library-tag-management-row .button-yellow"));
        _commands.DidNotReceive().RenameTag(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task ConfirmMerge_AfterCollision_DispatchesRenameAndCallback()
    {
        var entry = BuildEntry("a", ["bug", "defect"]);
        (string OldName, string NewName)? captured = null;
        var component = Render(
            allTags: ["bug", "defect"],
            entries: [entry],
            onRenamed: t => { captured = t; return Task.CompletedTask; });

        await BeginRenameAsync(component);
        await SetEditInputAsync(component, "defect");
        await component.Find(".library-tag-management-row .button-green").ClickAsync(new MouseEventArgs());

        await component.Find(".library-tag-management-row .button-yellow").ClickAsync(new MouseEventArgs());

        _commands.Received(1).RenameTag("bug", "defect");
        Assert.NotNull(captured);
    }

    [Fact]
    public async Task ConfirmRename_EditingNameAfterMergePrompt_ClearsMergeState()
    {
        var entry = BuildEntry("a", ["bug", "defect"]);
        var component = Render(allTags: ["bug", "defect"], entries: [entry]);

        await BeginRenameAsync(component);
        await SetEditInputAsync(component, "defect");
        await component.Find(".library-tag-management-row .button-green").ClickAsync(new MouseEventArgs());
        Assert.NotNull(component.Find(".library-tag-management-row .button-yellow"));

        await SetEditInputAsync(component, "unique");

        Assert.Empty(component.FindAll(".library-tag-management-row .button-yellow"));
    }

    [Fact]
    public async Task ConfirmRename_EmptyName_NoOp()
    {
        var entry = BuildEntry("a", ["bug"]);
        var component = Render(allTags: ["bug"], entries: [entry]);

        await BeginRenameAsync(component);
        await SetEditInputAsync(component, "  ");
        await component.Find(".library-tag-management-row .button-green").ClickAsync(new MouseEventArgs());

        _commands.DidNotReceive().RenameTag(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task ConfirmDelete_DispatchesDeleteTag_AndCallback()
    {
        var entry = BuildEntry("a", ["bug"]);
        string? capturedDeleted = null;
        var component = Render(
            allTags: ["bug"],
            entries: [entry],
            onDeleted: t => { capturedDeleted = t; return Task.CompletedTask; });

        await component.Find(".library-tag-management-row .button-red").ClickAsync(new MouseEventArgs());
        await component.Find(".library-tag-management-row .button-red.icon-button").ClickAsync(new MouseEventArgs());

        _commands.Received(1).DeleteTag("bug");
        Assert.Equal("bug", capturedDeleted);
    }

    private IRenderedComponent<TagManagementPanel> Render(
        IReadOnlyList<string> allTags,
        IReadOnlyList<LibraryEntry> entries,
        Func<(string OldName, string NewName), Task>? onRenamed = null,
        Func<string, Task>? onDeleted = null) =>
        Render<TagManagementPanel>(parameters => parameters
            .Add(p => p.AllLibraryTags, allTags)
            .Add(p => p.AllEntries, entries)
            .Add(p => p.OnTagRenamed, onRenamed ?? (_ => Task.CompletedTask))
            .Add(p => p.OnTagDeleted, onDeleted ?? (_ => Task.CompletedTask)));

    private static async Task BeginRenameAsync(IRenderedComponent<TagManagementPanel> component)
    {
        var row = component.Find(".library-tag-management-row");
        var renameButton = row.QuerySelectorAll(".icon-button")[0];
        await renameButton.ClickAsync(new MouseEventArgs());
    }

    private static async Task SetEditInputAsync(IRenderedComponent<TagManagementPanel> component, string value)
    {
        var input = component.Find(".library-tag-management-row-edit-input");
        await input.InputAsync(new ChangeEventArgs { Value = value });
    }

    private static LibraryEntrySavedFilter BuildEntry(string name, params string[] tags)
    {
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);

        return new LibraryEntrySavedFilter
        {
            Name = name,
            CreatedUtc = DateTimeOffset.UtcNow,
            Filter = filter,
            Origin = LibraryEntryOrigin.UserSaved,
            Tags = [.. tags],
        };
    }
}
