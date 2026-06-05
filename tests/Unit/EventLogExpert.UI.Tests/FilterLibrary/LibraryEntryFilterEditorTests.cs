// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.UI.FilterLibrary;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventLogExpert.UI.Tests.FilterLibrary;

public sealed class LibraryEntryFilterEditorTests : BunitContext
{
    private readonly IAlertDialogService _alerts = Substitute.For<IAlertDialogService>();
    private readonly IAnnouncementService _announcements = Substitute.For<IAnnouncementService>();
    private readonly IFilterLibraryCommands _commands = Substitute.For<IFilterLibraryCommands>();

    public LibraryEntryFilterEditorTests()
    {
        Services.AddSingleton(_alerts);
        Services.AddSingleton(_announcements);
        Services.AddSingleton(_commands);

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void AddFilter_ClickAddButton_RendersPendingLibraryFilterRow()
    {
        var filterSet = BuildFilterSet("Set");

        var component = Render<LibraryEntryFilterEditor>(p => p
            .Add(x => x.FilterSet, filterSet)
            .Add(x => x.IsExpanded, true));

        Assert.Empty(component.FindComponents<LibraryFilterRow>());

        component.Find(".library-entry-filter-editor-add-button").Click();

        Assert.Single(component.FindComponents<LibraryFilterRow>());
    }

    [Fact]
    public async Task AddFilter_ThenDiscardPendingDraft_RemovesPendingRowWithoutUpdate()
    {
        var filterSet = BuildFilterSet("Set");

        var component = Render<LibraryEntryFilterEditor>(p => p
            .Add(x => x.FilterSet, filterSet)
            .Add(x => x.IsExpanded, true));

        component.Find(".library-entry-filter-editor-add-button").Click();

        var pendingRow = component.FindComponents<LibraryFilterRow>()[0];
        await component.InvokeAsync(() => pendingRow.Instance.OnPendingDiscard.InvokeAsync());

        _commands.DidNotReceive().UpdateEntry(Arg.Any<LibraryEntry>());

        component.Render();
        Assert.Empty(component.FindComponents<LibraryFilterRow>());
    }

    [Fact]
    public async Task AddFilter_ThenSavePendingDraft_DispatchesUpdateEntryAndRemovesPendingRow()
    {
        var filterSet = BuildFilterSet("Set");
        var built = BuildFilter("Level == 7");

        var component = Render<LibraryEntryFilterEditor>(p => p
            .Add(x => x.FilterSet, filterSet)
            .Add(x => x.IsExpanded, true));

        component.Find(".library-entry-filter-editor-add-button").Click();

        var pendingRow = component.FindComponents<LibraryFilterRow>()[0];
        await component.InvokeAsync(() => pendingRow.Instance.OnPendingSave.InvokeAsync(built));

        var captured = CapturedUpdatedFilterSet();
        Assert.Single(captured.Filters);
        Assert.Equal("Level == 7", captured.Filters[0].ComparisonText);

        component.Render();
        Assert.Empty(component.FindComponents<LibraryFilterRow>());
    }

    [Fact]
    public void IsExpanded_False_HidesFilterList()
    {
        var filterSet = BuildFilterSet("Set", BuildFilter("Level == 4"));

        var component = Render<LibraryEntryFilterEditor>(p => p
            .Add(x => x.FilterSet, filterSet)
            .Add(x => x.IsExpanded, false));

        Assert.Empty(component.FindAll(".library-entry-filter-editor-list"));
    }

    [Fact]
    public void IsExpanded_True_RendersOneLibraryFilterRowPerFilter()
    {
        var filterSet = BuildFilterSet("Set", BuildFilter("Level == 4"), BuildFilter("Level == 5"));

        var component = Render<LibraryEntryFilterEditor>(p => p
            .Add(x => x.FilterSet, filterSet)
            .Add(x => x.IsExpanded, true));

        var rows = component.FindComponents<LibraryFilterRow>();
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void IsExpanded_True_ShowsFilterList()
    {
        var filterSet = BuildFilterSet("Set", BuildFilter("Level == 4"));

        var component = Render<LibraryEntryFilterEditor>(p => p
            .Add(x => x.FilterSet, filterSet)
            .Add(x => x.IsExpanded, true));

        Assert.Single(component.FindAll(".library-entry-filter-editor-list"));
    }

    [Fact]
    public async Task OnExclusionChangedForSaved_ImmediateCommit_DispatchesUpdateEntry()
    {
        var original = BuildFilter("Level == 4");
        var filterSet = BuildFilterSet("Set", original);

        var component = Render<LibraryEntryFilterEditor>(p => p
            .Add(x => x.FilterSet, filterSet)
            .Add(x => x.IsExpanded, true));

        var row = component.FindComponents<LibraryFilterRow>()[0];
        await component.InvokeAsync(() => row.Instance.OnExclusionChanged.InvokeAsync(true));

        var captured = CapturedUpdatedFilterSet();
        Assert.Single(captured.Filters);
        Assert.True(captured.Filters[0].IsExcluded);
    }

    [Fact]
    public async Task OnRemoveSaved_ImmediateCommit_DispatchesUpdateEntryWithoutFilter()
    {
        var kept = BuildFilter("Level == 4");
        var removed = BuildFilter("Level == 5");
        var filterSet = BuildFilterSet("Set", kept, removed);

        var component = Render<LibraryEntryFilterEditor>(p => p
            .Add(x => x.FilterSet, filterSet)
            .Add(x => x.IsExpanded, true));

        var rows = component.FindComponents<LibraryFilterRow>();
        await component.InvokeAsync(() => rows[1].Instance.OnRemove.InvokeAsync());

        var captured = CapturedUpdatedFilterSet();
        Assert.Single(captured.Filters);
        Assert.Equal("Level == 4", captured.Filters[0].ComparisonText);
    }

    [Fact]
    public async Task OnSaveSavedFilter_ImmediateCommit_DispatchesUpdateEntry()
    {
        var original = BuildFilter("Level == 4");
        var filterSet = BuildFilterSet("Set", original);
        var updatedFilter = BuildFilter("Level == 5");

        var component = Render<LibraryEntryFilterEditor>(p => p
            .Add(x => x.FilterSet, filterSet)
            .Add(x => x.IsExpanded, true));

        var row = component.FindComponents<LibraryFilterRow>()[0];
        await component.InvokeAsync(() => row.Instance.OnSave.InvokeAsync(updatedFilter));

        var captured = CapturedUpdatedFilterSet();
        Assert.Single(captured.Filters);
        Assert.Equal("Level == 5", captured.Filters[0].ComparisonText);
        Assert.Equal(original.Id, captured.Filters[0].Id);
    }

    [Fact]
    public async Task OnToggleEnabledForSaved_ImmediateCommit_DispatchesUpdateEntryWithFlippedEnabledFlag()
    {
        var original = BuildFilter("Level == 4", isEnabled: false);
        var filterSet = BuildFilterSet("Set", original);

        var component = Render<LibraryEntryFilterEditor>(p => p
            .Add(x => x.FilterSet, filterSet)
            .Add(x => x.IsExpanded, true));

        var row = component.FindComponents<LibraryFilterRow>()[0];
        await component.InvokeAsync(() => row.Instance.OnToggleEnabled.InvokeAsync());

        var captured = CapturedUpdatedFilterSet();
        Assert.Single(captured.Filters);
        Assert.True(captured.Filters[0].IsEnabled);
    }

    [Fact]
    public void SetLevelEditSaveCancelButtons_AreNotRendered()
    {
        var filterSet = BuildFilterSet("Set", BuildFilter("Level == 4"));

        var component = Render<LibraryEntryFilterEditor>(p => p
            .Add(x => x.FilterSet, filterSet)
            .Add(x => x.IsExpanded, true));

        Assert.Empty(component.FindAll(".library-entry-filter-editor-header"));
        Assert.Empty(component.FindAll(".library-entry-filter-editor-chevron"));
        Assert.Empty(component.FindAll(".library-entry-filter-editor-summary"));
    }

    [Fact]
    public async Task TwoPendingDrafts_SaveOne_OtherPreserved()
    {
        var filterSet = BuildFilterSet("Set");
        var builtA = BuildFilter("Level == 7");

        var component = Render<LibraryEntryFilterEditor>(p => p
            .Add(x => x.FilterSet, filterSet)
            .Add(x => x.IsExpanded, true));

        component.Find(".library-entry-filter-editor-add-button").Click();
        component.Find(".library-entry-filter-editor-add-button").Click();

        Assert.Equal(2, component.FindComponents<LibraryFilterRow>().Count);

        var rows = component.FindComponents<LibraryFilterRow>();
        await component.InvokeAsync(() => rows[0].Instance.OnPendingSave.InvokeAsync(builtA));

        component.Render();
        Assert.Single(component.FindComponents<LibraryFilterRow>());
    }

    private static SavedFilter BuildFilter(string comparisonText, bool isEnabled = true)
    {
        var filter = SavedFilter.TryCreate(
            comparisonText,
            color: HighlightColor.None,
            isEnabled: isEnabled,
            mode: FilterMode.Advanced);
        Assert.NotNull(filter);
        return filter;
    }

    private static LibraryEntryFilterSet BuildFilterSet(string name, params SavedFilter[] filters) =>
        new()
        {
            Name = name,
            CreatedUtc = DateTimeOffset.UtcNow,
            Filters = [.. filters],
        };

    private LibraryEntryFilterSet CapturedUpdatedFilterSet()
    {
        var call = _commands.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IFilterLibraryCommands.UpdateEntry));
        var entry = (LibraryEntry)call.GetArguments()[0]!;
        return Assert.IsType<LibraryEntryFilterSet>(entry);
    }
}
