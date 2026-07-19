// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.UI.FilterLibrary;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventLogExpert.UI.Tests.FilterLibrary;

public sealed class LibrarySavedTabHeaderTests : BunitContext
{
    private readonly IAnnouncementService _announcements = Substitute.For<IAnnouncementService>();
    private readonly IFilterLibraryCommands _commands = Substitute.For<IFilterLibraryCommands>();
    private FilterLibraryState _libraryState = new();

    public LibrarySavedTabHeaderTests()
    {
        Services.AddSingleton(_announcements);
        Services.AddSingleton(_commands);
        Services.AddSingleton(Substitute.For<IAlertDialogService>());

        var libraryStateMock = Substitute.For<IState<FilterLibraryState>>();
        libraryStateMock.Value.Returns(_ => _libraryState);
        Services.AddSingleton(libraryStateMock);

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public async Task ClickButton_ExpandsDraftPanel()
    {
        var component = Render(allTags: [], existing: []);

        await component.Find(".library-saved-tab-new-button").ClickAsync(new MouseEventArgs());

        Assert.NotNull(component.Find(".library-saved-tab-new-draft"));
        Assert.Empty(component.FindAll(".library-saved-tab-new-button"));
    }

    [Fact]
    public async Task Discard_ClearsState_CollapsesPanel()
    {
        var component = Render(allTags: [], existing: []);
        await component.Find(".library-saved-tab-new-button").ClickAsync(new MouseEventArgs());

        await SetNameAsync(component, "Draft");

        var row = component.FindComponent<LibraryFilterRow>();
        await component.InvokeAsync(() => row.Instance.OnPendingDiscard.InvokeAsync());

        Assert.NotNull(component.Find(".library-saved-tab-new-button"));
        _commands.DidNotReceive().AddEntry(Arg.Any<LibraryEntry>());
    }

    [Fact]
    public void IdleState_RendersCollapsedButton()
    {
        var component = Render(
            allTags: [],
            existing: []);

        Assert.NotNull(component.Find(".library-saved-tab-new-button"));
        Assert.Empty(component.FindAll(".library-saved-tab-new-draft"));
    }

    [Fact]
    public async Task NameValidates_OnEveryKeystroke()
    {
        var existing = BuildSavedFilter("Dup");
        var component = Render(allTags: [], existing: [existing]);
        await component.Find(".library-saved-tab-new-button").ClickAsync(new MouseEventArgs());

        await SetNameAsync(component, "Dup");

        Assert.Contains("already exists", component.Find(".library-saved-tab-new-draft-error").TextContent);

        await SetNameAsync(component, "Unique");

        Assert.Empty(component.FindAll(".library-saved-tab-new-draft-error"));
    }

    [Fact]
    public async Task SaveWithDuplicateName_ShowsValidationError_DoesNotAddEntry()
    {
        var existing = BuildSavedFilter("Dup");
        var component = Render(allTags: [], existing: [existing]);
        await component.Find(".library-saved-tab-new-button").ClickAsync(new MouseEventArgs());

        await SetNameAsync(component, "Dup");
        await TriggerRowPendingSaveAsync(component, SavedFilter.TryCreate("Level == 4")!);

        Assert.Contains("already exists", component.Find(".library-saved-tab-new-draft-error").TextContent);
        _commands.DidNotReceive().AddEntry(Arg.Any<LibraryEntry>());
    }

    [Fact]
    public async Task SaveWithMissingName_ShowsValidationError_DoesNotAddEntry()
    {
        var component = Render(allTags: [], existing: []);
        await component.Find(".library-saved-tab-new-button").ClickAsync(new MouseEventArgs());

        await TriggerRowPendingSaveAsync(component, SavedFilter.TryCreate("Level == 4")!);

        Assert.NotNull(component.Find(".library-saved-tab-new-draft-error"));
        _commands.DidNotReceive().AddEntry(Arg.Any<LibraryEntry>());
    }

    [Fact]
    public async Task SaveWithNameMatchingExistingFilterSet_AddsEntry()
    {
        _libraryState = _libraryState with { Entries = [BuildFilterSet("Dup")] };
        var component = Render(allTags: [], existing: []);
        await component.Find(".library-saved-tab-new-button").ClickAsync(new MouseEventArgs());

        await SetNameAsync(component, "Dup");
        await TriggerRowPendingSaveAsync(component, SavedFilter.TryCreate("Level == 4")!);

        Assert.Empty(component.FindAll(".library-saved-tab-new-draft-error"));
        _commands.Received(1).AddEntry(Arg.Any<LibraryEntry>());
    }

    [Fact]
    public async Task ValidName_AndCommittedFilter_AddsEntryAndCollapses()
    {
        var component = Render(allTags: [], existing: []);
        await component.Find(".library-saved-tab-new-button").ClickAsync(new MouseEventArgs());

        await SetNameAsync(component, "My New");
        var built = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(built);
        await TriggerRowPendingSaveAsync(component, built);

        var captured = (LibraryEntrySavedFilter?)_commands.ReceivedCalls()
            .FirstOrDefault(c => c.GetMethodInfo().Name == nameof(IFilterLibraryCommands.AddEntry))
            ?.GetArguments()[0];

        Assert.NotNull(captured);
        Assert.Equal("My New", captured.Name);
        Assert.Equal(LibraryEntryOrigin.UserSaved, captured.Origin);
        _announcements.Received().Announce(Arg.Is<string>(s => s != null && s.Contains("My New")));
        Assert.NotNull(component.Find(".library-saved-tab-new-button"));
    }

    private static LibraryEntryFilterSet BuildFilterSet(string name)
    {
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);

        return new LibraryEntryFilterSet
        {
            Name = name,
            CreatedUtc = DateTimeOffset.UtcNow,
            Filters = [filter],
            Origin = LibraryEntryOrigin.UserSaved,
        };
    }

    private static LibraryEntrySavedFilter BuildSavedFilter(string name)
    {
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);

        return new LibraryEntrySavedFilter
        {
            Name = name,
            CreatedUtc = DateTimeOffset.UtcNow,
            Filter = filter,
            Origin = LibraryEntryOrigin.UserSaved,
        };
    }

    private static async Task SetNameAsync(IRenderedComponent<LibrarySavedTabHeader> component, string text)
    {
        var input = component.Find("input[type='text']");
        await input.InputAsync(new ChangeEventArgs { Value = text });
    }

    private static async Task TriggerRowPendingSaveAsync(IRenderedComponent<LibrarySavedTabHeader> component, SavedFilter built)
    {
        var row = component.FindComponent<LibraryFilterRow>();
        await component.InvokeAsync(() => row.Instance.OnPendingSave.InvokeAsync(built));
    }

    private IRenderedComponent<LibrarySavedTabHeader> Render(
        IReadOnlyList<string> allTags,
        IReadOnlyList<LibraryEntrySavedFilter> existing) =>
        Render<LibrarySavedTabHeader>(parameters => parameters
            .Add(p => p.AllLibraryTags, allTags)
            .Add(p => p.ExistingSavedFilters, existing));
}
