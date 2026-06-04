// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.UI.FilterLibrary;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventLogExpert.UI.Tests.FilterLibrary;

public sealed class LibraryEntryFilterEditorTests : BunitContext
{
    private readonly IAlertDialogService _alerts = Substitute.For<IAlertDialogService>();
    private readonly IFilterLibraryCommands _commands = Substitute.For<IFilterLibraryCommands>();
    private readonly IState<FilterPaneState> _paneStateMock = Substitute.For<IState<FilterPaneState>>();
    private readonly FilterPaneState _paneStateValue = new();

    public LibraryEntryFilterEditorTests()
    {
        Services.AddSingleton(_alerts);
        Services.AddSingleton(_commands);

        _paneStateMock.Value.Returns(_ => _paneStateValue);
        Services.AddSingleton(_paneStateMock);

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void AddFilterButton_CreatesPendingDraftRow()
    {
        var filterSet = BuildFilterSet("Set", BuildFilter("Level == 4"));

        var component = Render<LibraryEntryFilterEditor>(p => p.Add(x => x.FilterSet, filterSet));
        component.Find(".library-entry-filter-editor-chevron").Click();
        component.Find(".library-entry-filter-editor-header .button:not(.icon-button)").Click();

        component.Find(".library-entry-filter-editor-add-button").Click();

        Assert.Single(component.FindAll(".library-entry-filter-editor-row-pending"));
    }

    [Fact]
    public void CancelAfterEdit_DoesNotDispatchUpdateEntry()
    {
        var filterSet = BuildFilterSet("Set", BuildFilter("Level == 4"));

        var component = Render<LibraryEntryFilterEditor>(p => p.Add(x => x.FilterSet, filterSet));
        component.Find(".library-entry-filter-editor-chevron").Click();
        component.Find(".library-entry-filter-editor-header .button:not(.icon-button)").Click();

        component.Find(".library-entry-filter-editor-input").Input("Level == 99");

        component.FindAll(".library-entry-filter-editor-header .button")
            .First(b => b.TextContent.Trim().Contains("Cancel", StringComparison.Ordinal))
            .Click();

        _commands.DidNotReceive().UpdateEntry(Arg.Any<LibraryEntry>());
    }

    [Fact]
    public void ChevronClick_ExpandsAndAriaExpandedToggles()
    {
        var filterSet = BuildFilterSet("Set", BuildFilter("Level == 4"));

        var component = Render<LibraryEntryFilterEditor>(p => p.Add(x => x.FilterSet, filterSet));

        component.Find(".library-entry-filter-editor-chevron").Click();

        var chevron = component.Find(".library-entry-filter-editor-chevron");
        Assert.Equal("true", chevron.GetAttribute("aria-expanded"));
        Assert.NotNull(chevron.GetAttribute("aria-controls"));
        Assert.Single(component.FindAll(".library-entry-filter-editor-list"));
    }

    [Fact]
    public async Task CollapseWithUnsavedWork_PromptsConfirmation()
    {
        _alerts.ShowAlert(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromResult(false));

        var filterSet = BuildFilterSet("Set", BuildFilter("Level == 4"));

        var component = Render<LibraryEntryFilterEditor>(p => p.Add(x => x.FilterSet, filterSet));
        component.Find(".library-entry-filter-editor-chevron").Click();
        component.Find(".library-entry-filter-editor-header .button:not(.icon-button)").Click();
        component.Find(".library-entry-filter-editor-input").Input("Level == 99");

        await component.InvokeAsync(() => component.Find(".library-entry-filter-editor-chevron").Click());

        await _alerts.Received(1).ShowAlert(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>());

        var chevron = component.Find(".library-entry-filter-editor-chevron");
        Assert.Equal("true", chevron.GetAttribute("aria-expanded"));
    }

    [Fact]
    public async Task CollapseWithUnsavedWork_UserConfirmsDiscard_Collapses()
    {
        _alerts.ShowAlert(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromResult(true));

        var filterSet = BuildFilterSet("Set", BuildFilter("Level == 4"));

        var component = Render<LibraryEntryFilterEditor>(p => p.Add(x => x.FilterSet, filterSet));
        component.Find(".library-entry-filter-editor-chevron").Click();
        component.Find(".library-entry-filter-editor-header .button:not(.icon-button)").Click();
        component.Find(".library-entry-filter-editor-input").Input("Level == 99");

        await component.InvokeAsync(() => component.Find(".library-entry-filter-editor-chevron").Click());

        var chevron = component.Find(".library-entry-filter-editor-chevron");
        Assert.Equal("false", chevron.GetAttribute("aria-expanded"));
    }

    [Fact]
    public void ExpandedReadOnlyView_ShowsComparisonTextPerFilter()
    {
        var filterSet = BuildFilterSet("Set", BuildFilter("Level == 4"), BuildFilter("Level == 5"));

        var component = Render<LibraryEntryFilterEditor>(p => p.Add(x => x.FilterSet, filterSet));
        component.Find(".library-entry-filter-editor-chevron").Click();

        var comparisons = component.FindAll(".library-entry-filter-editor-comparison");
        Assert.Equal(2, comparisons.Count);
        Assert.Contains(comparisons, c => c.TextContent.Contains("Level == 4", StringComparison.Ordinal));
        Assert.Contains(comparisons, c => c.TextContent.Contains("Level == 5", StringComparison.Ordinal));
    }

    [Fact]
    public void RemoveRow_MarksForRemoval_SaveOmitsThatFilter()
    {
        var keptFilter = BuildFilter("Level == 4");
        var removedFilter = BuildFilter("Level == 5");
        var filterSet = BuildFilterSet("Set", keptFilter, removedFilter);

        var component = Render<LibraryEntryFilterEditor>(p => p.Add(x => x.FilterSet, filterSet));
        component.Find(".library-entry-filter-editor-chevron").Click();
        component.Find(".library-entry-filter-editor-header .button:not(.icon-button)").Click();

        var removeButtons = component.FindAll(".library-entry-filter-editor-row .button-red");
        removeButtons.Last().Click();

        component.FindAll(".library-entry-filter-editor-header .button")
            .First(b => b.TextContent.Trim().Contains("Save", StringComparison.Ordinal))
            .Click();

        _commands.Received(1).UpdateEntry(Arg.Is<LibraryEntry>(e =>
            e is LibraryEntryFilterSet &&
            ((LibraryEntryFilterSet)e).Filters.Count == 1 &&
            ((LibraryEntryFilterSet)e).Filters[0].ComparisonText == "Level == 4"));
    }

    [Fact]
    public void RendersCollapsedByDefault_FilterListNotShown()
    {
        var filterSet = BuildFilterSet("Set", BuildFilter("Level == 4"));

        var component = Render<LibraryEntryFilterEditor>(p => p.Add(x => x.FilterSet, filterSet));

        var chevron = component.Find(".library-entry-filter-editor-chevron");
        Assert.Equal("false", chevron.GetAttribute("aria-expanded"));
        Assert.Empty(component.FindAll(".library-entry-filter-editor-list"));
    }

    [Fact]
    public void SaveAfterEdit_DispatchesUpdateEntry_FilterPaneStateNotMutated()
    {
        var originalFilter = BuildFilter("Level == 4");
        var filterSet = BuildFilterSet("Set", originalFilter);

        var component = Render<LibraryEntryFilterEditor>(p => p.Add(x => x.FilterSet, filterSet));
        component.Find(".library-entry-filter-editor-chevron").Click();
        component.Find(".library-entry-filter-editor-header .button:not(.icon-button)").Click();

        var input = component.Find(".library-entry-filter-editor-input");
        input.Input("Level == 5");

        component.FindAll(".library-entry-filter-editor-header .button")
            .First(b => b.TextContent.Trim().Contains("Save", StringComparison.Ordinal))
            .Click();

        _commands.Received(1).UpdateEntry(Arg.Is<LibraryEntry>(e =>
            e.Id == filterSet.Id &&
            e is LibraryEntryFilterSet &&
            ((LibraryEntryFilterSet)e).Filters.Count == 1 &&
            ((LibraryEntryFilterSet)e).Filters[0].ComparisonText == "Level == 5"));

        Assert.Empty(_paneStateValue.Filters);
    }

    private static SavedFilter BuildFilter(string comparisonText)
    {
        var filter = SavedFilter.TryCreate(comparisonText, color: HighlightColor.None, mode: FilterMode.Advanced);
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
}
