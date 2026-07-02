// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using AngleSharp.Dom;
using Bunit;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.DebugLog;
using EventLogExpert.Runtime.Modal;
using EventLogExpert.UI.DebugLog;
using EventLogExpert.UI.Tests.TestUtils;
using EventLogExpert.UI.Tests.TestUtils.Constants;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace EventLogExpert.UI.Tests.DebugLog;

public sealed class DebugLogModalTests : BunitContext
{
    private readonly IAlertDialogService _alertDialogService = Substitute.For<IAlertDialogService>();
    private readonly IClipboardService _clipboardService = Substitute.For<IClipboardService>();
    private readonly IDebugLogReader _debugLogReader = Substitute.For<IDebugLogReader>();
    private readonly IFileSaveService _fileSaveService = Substitute.For<IFileSaveService>();
    private readonly IModalCoordinator _modalCoordinator = Substitute.For<IModalCoordinator>();
    private readonly IModalService _modalService = Substitute.For<IModalService>();

    public DebugLogModalTests()
    {
        Services.AddBannerHostDependencies();
        Services.AddMenuMocks();

        _modalService.ActiveModalId.Returns(new ModalId(1L));

        Services.AddSingleton(_alertDialogService);
        Services.AddSingleton(_clipboardService);
        Services.AddSingleton(_debugLogReader);
        Services.AddSingleton(_fileSaveService);
        Services.AddSingleton(_modalCoordinator);
        Services.AddSingleton(_modalService);
        Services.AddFluxor(options => options.ScanAssemblies(typeof(DebugLogModal).Assembly));

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public async Task DebugLogModal_AddFilter_DisabledUntilCurrentFilterComplete()
    {
        _debugLogReader.LoadAsync(Arg.Any<CancellationToken>()).Returns(
            DebugLogUtils.ToAsyncEnumerable([DebugLogUtils.BuildLine(LogLevel.Information, "alpha foo")]));

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("1 of 1 entry", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        await AddFilterAsync(component);

        Assert.True(FindButton(component, "Add Filter").HasAttribute("disabled"));

        await component.Find("input[aria-label='Filter value']").ChangeAsync(new ChangeEventArgs { Value = "foo" });

        Assert.False(FindButton(component, "Add Filter").HasAttribute("disabled"));
    }

    [Fact]
    public async Task DebugLogModal_AddFilter_OpensAnEditor()
    {
        _debugLogReader.LoadAsync(Arg.Any<CancellationToken>()).Returns(
            DebugLogUtils.ToAsyncEnumerable([DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogFirstMessage)]));

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("1 of 1 entry", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        Assert.Empty(component.FindAll(".debug-log-filter-editor"));
        Assert.Empty(component.FindAll(".debug-log-filter-chip"));

        await AddFilterAsync(component);

        Assert.Single(component.FindAll(".debug-log-filter-editor"));
    }

    [Fact]
    public async Task DebugLogModal_AddingSecondFilter_LeavesOnlyOneEditorOpen()
    {
        var lines = new[]
        {
            DebugLogUtils.BuildLine(LogLevel.Information, "alpha foo"),
            DebugLogUtils.BuildLine(LogLevel.Information, "bravo bar"),
        };

        _debugLogReader.LoadAsync(Arg.Any<CancellationToken>()).Returns(DebugLogUtils.ToAsyncEnumerable(lines));

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("2 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        await AddFilterAsync(component);
        await component.Find("input[aria-label='Filter value']").ChangeAsync(new ChangeEventArgs { Value = "foo" });
        await component.Find("button[aria-label='Done editing filter']").ClickAsync(new MouseEventArgs());

        await component.WaitForAssertionAsync(() => Assert.Single(component.FindAll(".debug-log-filter-chip")));

        await AddFilterAsync(component);

        Assert.Single(component.FindAll(".debug-log-filter-editor"));
        Assert.Single(component.FindAll(".debug-log-filter-chip"));
    }

    [Fact]
    public async Task DebugLogModal_AfterLoad_FooterCounterIsPoliteLiveStatusRegion()
    {
        var lines = new[] { DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogFirstMessage) };
        _debugLogReader.LoadAsync(Arg.Any<CancellationToken>()).Returns(DebugLogUtils.ToAsyncEnumerable(lines));

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("1 of 1 entry", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        var counter = component.Find(".debug-log-footer-counter");
        Assert.Equal("status", counter.GetAttribute("role"));
        Assert.Equal("polite", counter.GetAttribute("aria-live"));
        Assert.Equal("true", counter.GetAttribute("aria-atomic"));
    }

    [Fact]
    public async Task DebugLogModal_AfterLoad_VirtualizeItemsAreNewestFirstAndPreserveContinuationLineOrder()
    {
        var firstHeader = DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogFirstMessage);
        var secondHeader = DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogSecondMessage);
        const string ContinuationLine = "  at MyMethod()";
        var thirdHeader = DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogThirdMessage);

        _debugLogReader.LoadAsync(Arg.Any<CancellationToken>()).Returns(
            DebugLogUtils.ToAsyncEnumerable([firstHeader, secondHeader, ContinuationLine, thirdHeader]));

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("3 of 3 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        var rows = component.FindAll(".debug-log-row");
        Assert.Equal(
            new[] { thirdHeader, secondHeader, ContinuationLine, firstHeader },
            rows.Select(row => row.TextContent).ToArray());
    }

    [Fact]
    public async Task DebugLogModal_CategoryEqualsFilter_NarrowsToSelectedCategory()
    {
        var lines = new[]
        {
            DebugLogUtils.BuildLine(LogLevel.Warning, "DatabaseTools.Create", "create line"),
            DebugLogUtils.BuildLine(LogLevel.Warning, "Elevation.Ipc", "ipc line"),
        };

        _debugLogReader.LoadAsync(Arg.Any<CancellationToken>()).Returns(DebugLogUtils.ToAsyncEnumerable(lines));

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("2 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        await AddFilterAsync(component);
        await SelectOptionAsync(component, "Filter field", "Category");
        await SelectOptionAsync(component, "Filter value", "DatabaseTools.Create");

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("1 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));
    }

    [Fact]
    public async Task DebugLogModal_CategoryEqualsUncategorized_NarrowsToNullCategoryEntries()
    {
        var lines = new[]
        {
            DebugLogUtils.BuildLine(LogLevel.Warning, "DatabaseTools.Create", "categorized"),
            DebugLogUtils.BuildLine(LogLevel.Warning, "uncategorized"),
        };

        _debugLogReader.LoadAsync(Arg.Any<CancellationToken>()).Returns(DebugLogUtils.ToAsyncEnumerable(lines));

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("2 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        await AddFilterAsync(component);
        await SelectOptionAsync(component, "Filter field", "Category");
        await SelectOptionAsync(component, "Filter value", "(Uncategorized)");

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("1 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));
    }

    [Fact]
    public async Task DebugLogModal_CategoryFieldSelectedButNoValue_ShowsBlankNotUncategorized()
    {
        var lines = new[]
        {
            DebugLogUtils.BuildLine(LogLevel.Warning, "DatabaseTools.Create", "categorized"),
            DebugLogUtils.BuildLine(LogLevel.Warning, "uncategorized"),
        };

        _debugLogReader.LoadAsync(Arg.Any<CancellationToken>()).Returns(DebugLogUtils.ToAsyncEnumerable(lines));

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("2 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        await AddFilterAsync(component);
        await SelectOptionAsync(component, "Filter field", "Category");

        // Regression: with nothing selected the value header must be blank, not the "(Uncategorized)"
        // sentinel label, and the incomplete filter stays a no-op (all entries remain visible).
        var valueHeader = component.Find("input[aria-label='Filter value']");
        Assert.True(
            string.IsNullOrEmpty(valueHeader.GetAttribute("value")),
            "Category value header must be blank when nothing is selected.");
        Assert.Equal("2 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim());

        await SelectOptionAsync(component, "Filter value", "(Uncategorized)");

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("(Uncategorized)", component.Find("input[aria-label='Filter value']").GetAttribute("value")));
        await component.WaitForAssertionAsync(() =>
            Assert.Equal("1 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));
    }

    [Fact]
    public async Task DebugLogModal_Clear_ClearsTheLogFile()
    {
        _debugLogReader.LoadAsync(Arg.Any<CancellationToken>()).Returns(
            DebugLogUtils.ToAsyncEnumerable([DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogFirstMessage)]));

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("1 of 1 entry", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        await FindButton(component, "Clear").ClickAsync(new MouseEventArgs());

        await _debugLogReader.Received(1).ClearAsync();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("0 of 0 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));
    }

    [Fact]
    public async Task DebugLogModal_Copy_CopiesTheDisplayedView()
    {
        _debugLogReader.LoadAsync(Arg.Any<CancellationToken>()).Returns(
            DebugLogUtils.ToAsyncEnumerable([DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogFirstMessage)]));

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("1 of 1 entry", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        await FindButton(component, "Copy").ClickAsync(new MouseEventArgs());

        await _clipboardService.Received(1).CopyTextAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task DebugLogModal_DoneEditing_CollapsesToChipWithSummary()
    {
        var lines = new[]
        {
            DebugLogUtils.BuildLine(LogLevel.Information, "alpha foo"),
            DebugLogUtils.BuildLine(LogLevel.Information, "bravo"),
        };

        _debugLogReader.LoadAsync(Arg.Any<CancellationToken>()).Returns(DebugLogUtils.ToAsyncEnumerable(lines));

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("2 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        await AddFilterAsync(component);
        await component.Find("input[aria-label='Filter value']").ChangeAsync(new ChangeEventArgs { Value = "foo" });

        Assert.Single(component.FindAll(".debug-log-filter-editor"));

        await component.Find("button[aria-label='Done editing filter']").ClickAsync(new MouseEventArgs());

        await component.WaitForAssertionAsync(() => Assert.Single(component.FindAll(".debug-log-filter-chip")));
        Assert.Empty(component.FindAll(".debug-log-filter-editor"));
        Assert.Equal("Message contains foo", component.Find(".debug-log-filter-summary").TextContent.Trim());
        Assert.Equal("1 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim());
    }

    [Fact]
    public async Task DebugLogModal_EditChip_ReopensTheEditor()
    {
        var lines = new[]
        {
            DebugLogUtils.BuildLine(LogLevel.Information, "alpha foo"),
            DebugLogUtils.BuildLine(LogLevel.Information, "bravo"),
        };

        _debugLogReader.LoadAsync(Arg.Any<CancellationToken>()).Returns(DebugLogUtils.ToAsyncEnumerable(lines));

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("2 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        await AddFilterAsync(component);
        await component.Find("input[aria-label='Filter value']").ChangeAsync(new ChangeEventArgs { Value = "foo" });
        await component.Find("button[aria-label='Done editing filter']").ClickAsync(new MouseEventArgs());

        await component.WaitForAssertionAsync(() => Assert.Single(component.FindAll(".debug-log-filter-chip")));

        await component.Find("button[aria-label='Edit filter: Message contains foo']").ClickAsync(new MouseEventArgs());

        Assert.Single(component.FindAll(".debug-log-filter-editor"));
        Assert.Empty(component.FindAll(".debug-log-filter-chip"));
    }

    [Fact]
    public async Task DebugLogModal_EditingAnotherChipWhileIncomplete_LeavesPlaceholderChipAndGatesAdd()
    {
        var lines = new[]
        {
            DebugLogUtils.BuildLine(LogLevel.Information, "alpha foo"),
            DebugLogUtils.BuildLine(LogLevel.Information, "bravo"),
        };

        _debugLogReader.LoadAsync(Arg.Any<CancellationToken>()).Returns(DebugLogUtils.ToAsyncEnumerable(lines));

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("2 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        await AddFilterAsync(component);
        await component.Find("input[aria-label='Filter value']").ChangeAsync(new ChangeEventArgs { Value = "foo" });
        await component.Find("button[aria-label='Done editing filter']").ClickAsync(new MouseEventArgs());

        await component.WaitForAssertionAsync(() => Assert.Single(component.FindAll(".debug-log-filter-chip")));

        // Add a fresh (incomplete) filter, then edit the existing complete chip. The abandoned incomplete
        // draft collapses to a placeholder "?" chip (a projection no-op) and Add stays gated.
        await AddFilterAsync(component);
        await component.Find("button[aria-label='Edit filter: Message contains foo']").ClickAsync(new MouseEventArgs());

        await component.WaitForAssertionAsync(() => Assert.Single(component.FindAll(".debug-log-filter-editor")));

        var chips = component.FindAll(".debug-log-filter-chip");
        Assert.Single(chips);
        Assert.Contains("?", chips[0].TextContent);
        Assert.True(FindButton(component, "Add Filter").HasAttribute("disabled"));
        Assert.Equal("1 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim());
    }

    [Fact]
    public async Task DebugLogModal_ExcludeMessageContains_HidesMatchingEntries()
    {
        var lines = new[]
        {
            DebugLogUtils.BuildLine(LogLevel.Information, "has foo"),
            DebugLogUtils.BuildLine(LogLevel.Information, "no match"),
        };

        _debugLogReader.LoadAsync(Arg.Any<CancellationToken>()).Returns(DebugLogUtils.ToAsyncEnumerable(lines));

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("2 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        await AddFilterAsync(component);
        await component.Find("button[aria-label='Filter is included; activate to exclude']").ClickAsync(new MouseEventArgs());
        await component.Find("input[aria-label='Filter value']").ChangeAsync(new ChangeEventArgs { Value = "foo" });

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("1 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));
    }

    [Fact]
    public async Task DebugLogModal_MessageContainsFilter_NarrowsToMatchingEntries()
    {
        var lines = new[]
        {
            DebugLogUtils.BuildLine(LogLevel.Information, "alpha foo bravo"),
            DebugLogUtils.BuildLine(LogLevel.Information, "charlie delta"),
        };

        _debugLogReader.LoadAsync(Arg.Any<CancellationToken>()).Returns(DebugLogUtils.ToAsyncEnumerable(lines));

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("2 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        await AddFilterAsync(component);

        await component.Find("input[aria-label='Filter value']").ChangeAsync(new ChangeEventArgs { Value = "foo" });

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("1 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));
    }

    [Fact]
    public async Task DebugLogModal_MultiSelectThenSingleOperator_DropsStaleValues()
    {
        var lines = new[]
        {
            DebugLogUtils.BuildLine(LogLevel.Error, "err"),
            DebugLogUtils.BuildLine(LogLevel.Warning, "warn"),
            DebugLogUtils.BuildLine(LogLevel.Information, "info"),
        };

        _debugLogReader.LoadAsync(Arg.Any<CancellationToken>()).Returns(DebugLogUtils.ToAsyncEnumerable(lines));

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("3 of 3 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        await AddFilterAsync(component);
        await SelectOptionAsync(component, "Filter field", "Level");
        await SelectOptionAsync(component, "Filter operator", "Multi Select");
        await SelectOptionAsync(component, "Filter value", "Error");
        await SelectOptionAsync(component, "Filter value", "Warning");

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("2 of 3 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        await SelectOptionAsync(component, "Filter operator", "Equals");

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("1 of 3 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));
    }

    [Fact]
    public async Task DebugLogModal_ProcessEqualsFilter_NarrowsToSelectedOrigin()
    {
        var lines = new[]
        {
            DebugLogUtils.BuildElevatedHelperLine(LogLevel.Warning, "helper line"),
            DebugLogUtils.BuildLine(LogLevel.Warning, "in-process line"),
        };

        _debugLogReader.LoadAsync(Arg.Any<CancellationToken>()).Returns(DebugLogUtils.ToAsyncEnumerable(lines));

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("2 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        await AddFilterAsync(component);
        await SelectOptionAsync(component, "Filter field", "Process");
        await SelectOptionAsync(component, "Filter value", "Elevated helper");

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("1 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));
    }

    [Fact]
    public async Task DebugLogModal_RemoveFilter_RestoresAllEntries()
    {
        var lines = new[]
        {
            DebugLogUtils.BuildLine(LogLevel.Information, "alpha foo bravo"),
            DebugLogUtils.BuildLine(LogLevel.Information, "charlie delta"),
        };

        _debugLogReader.LoadAsync(Arg.Any<CancellationToken>()).Returns(DebugLogUtils.ToAsyncEnumerable(lines));

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("2 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        await AddFilterAsync(component);
        await component.Find("input[aria-label='Filter value']").ChangeAsync(new ChangeEventArgs { Value = "foo" });

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("1 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        await component.Find("button[aria-label^='Remove filter']").ClickAsync(new MouseEventArgs());

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("2 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));
    }

    [Fact]
    public async Task DebugLogModal_WhenLogEmpty_ShowsEmptyState()
    {
        _debugLogReader.LoadAsync(Arg.Any<CancellationToken>()).Returns(DebugLogUtils.ToAsyncEnumerable([]));

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("0 of 0 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        Assert.Contains("Log is Empty", component.Markup);
    }

    private static async Task AddFilterAsync(IRenderedComponent<DebugLogModal> component) =>
        await FindButton(component, "Add Filter").ClickAsync(new MouseEventArgs());

    private static IElement FindButton(IRenderedComponent<DebugLogModal> component, string text) =>
        component.FindAll("button").First(button => button.TextContent.Contains(text));

    private static async Task SelectOptionAsync(IRenderedComponent<DebugLogModal> component, string ariaLabel, string optionText)
    {
        var dropdown = component.FindAll(".dropdown-input")
            .First(element => element.QuerySelector($"input[aria-label='{ariaLabel}']") is not null);

        var option = dropdown.QuerySelectorAll("[role='option']")
            .First(item => item.TextContent.Trim() == optionText);

        await option.MouseDownAsync(new MouseEventArgs());
    }
}
