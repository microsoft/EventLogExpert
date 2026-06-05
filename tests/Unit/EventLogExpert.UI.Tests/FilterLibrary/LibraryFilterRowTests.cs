// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Filtering.Drafts;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.UI.FilterLibrary;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventLogExpert.UI.Tests.FilterLibrary;

public sealed class LibraryFilterRowTests : BunitContext
{
    private readonly IAlertDialogService _alerts = Substitute.For<IAlertDialogService>();
    private readonly IAnnouncementService _announcements = Substitute.For<IAnnouncementService>();

    public LibraryFilterRowTests()
    {
        Services.AddSingleton(_alerts);
        Services.AddSingleton(_announcements);

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public async Task OnExclusionChanged_ForwardsBoolToParent()
    {
        var saved = BuildSavedFilter("Level == 4");
        bool? received = null;

        var component = Render<LibraryFilterRow>(p => p
            .Add(x => x.Value, saved)
            .Add(x => x.OnExclusionChanged, EventCallback.Factory.Create<bool>(this, b => received = b)));

        await component.Instance.OnExclusionChanged.InvokeAsync(true);

        Assert.True(received);
    }

    [Fact]
    public async Task OnPendingDiscard_ForwardsToParent()
    {
        var draft = new FilterDraft { Mode = FilterMode.Advanced };
        bool invoked = false;

        var component = Render<LibraryFilterRow>(p => p
            .Add(x => x.PendingDraft, draft)
            .Add(x => x.OnPendingDiscard, EventCallback.Factory.Create(this, () => invoked = true)));

        await component.Instance.OnPendingDiscard.InvokeAsync();

        Assert.True(invoked);
    }

    [Fact]
    public async Task OnPendingSave_ForwardsBuiltSavedFilterToParent()
    {
        var draft = new FilterDraft { Mode = FilterMode.Advanced, ComparisonText = "Level == 1" };
        var built = BuildSavedFilter("Level == 1");
        SavedFilter? received = null;

        var component = Render<LibraryFilterRow>(p => p
            .Add(x => x.PendingDraft, draft)
            .Add(x => x.OnPendingSave, EventCallback.Factory.Create<SavedFilter>(this, f => received = f)));

        await component.Instance.OnPendingSave.InvokeAsync(built);

        Assert.Same(built, received);
    }

    [Fact]
    public async Task OnRemove_ForwardsToParent()
    {
        var saved = BuildSavedFilter("Level == 4");
        bool invoked = false;

        var component = Render<LibraryFilterRow>(p => p
            .Add(x => x.Value, saved)
            .Add(x => x.OnRemove, EventCallback.Factory.Create(this, () => invoked = true)));

        await component.Instance.OnRemove.InvokeAsync();

        Assert.True(invoked);
    }

    [Fact]
    public async Task OnSave_ForwardsToParent()
    {
        var saved = BuildSavedFilter("Level == 4");
        SavedFilter? received = null;

        var component = Render<LibraryFilterRow>(p => p
            .Add(x => x.Value, saved)
            .Add(x => x.OnSave, EventCallback.Factory.Create<SavedFilter>(this, f => received = f)));

        await component.Instance.OnSave.InvokeAsync(saved);

        Assert.Same(saved, received);
    }

    [Fact]
    public void Render_WithPendingDraft_IsEditingTrue()
    {
        var draft = new FilterDraft { Mode = FilterMode.Advanced, ComparisonText = "Level == 1" };

        var component = Render<LibraryFilterRow>(p => p
            .Add(x => x.PendingDraft, draft));

        Assert.True(component.Instance.IsEditing);
    }

    [Fact]
    public void Render_WithSavedValue_DelegatesToFilterEditorCore_NoFilterPaneCommandsResolvedFromDI()
    {
        var saved = BuildSavedFilter("Level == 4");

        var component = Render<LibraryFilterRow>(p => p
            .Add(x => x.Value, saved));

        Assert.False(component.Instance.IsEditing);
        Assert.Null(Services.GetService<IFilterPaneCommands>());
    }

    private static SavedFilter BuildSavedFilter(string text) =>
        new()
        {
            ComparisonText = text,
            Compiled = null,
            IsEnabled = true,
        };
}
