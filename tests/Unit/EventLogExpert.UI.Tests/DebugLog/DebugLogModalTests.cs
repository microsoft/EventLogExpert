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

        // The reader yields NEWEST-first, so the mock returns the file's lines reversed; the parser reassembles the
        // second entry's continuation and the viewer renders newest-first with continuation lines in natural order.
        _debugLogReader.LoadAsync(Arg.Any<CancellationToken>()).Returns(
            DebugLogUtils.ToAsyncEnumerable([thirdHeader, ContinuationLine, secondHeader, firstHeader]));

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("3 of 3 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        var rows = component.FindAll(".debug-log-row");
        Assert.Equal(
            new[] { thirdHeader, secondHeader, ContinuationLine, firstHeader },
            rows.Select(row => row.TextContent).ToArray());
    }

    [Fact]
    public async Task DebugLogModal_CancelEditing_EditedChip_RevertsToAppliedForm()
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

        // Apply A (contains "foo") -> 1 of 2, chip shows "Message contains foo".
        await AddFilterAsync(component);
        await component.Find("input[aria-label='Filter value']").ChangeAsync(new ChangeEventArgs { Value = "foo" });
        await SaveFilterAsync(component);

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("1 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        // Re-open the chip (edit-on-copy) and change the value to "bar", then Cancel. The applied form must be
        // untouched: the chip still reads "Message contains foo" and the projection stays 1 of 2.
        await component.Find(".debug-log-filter-chip-edit").ClickAsync(new MouseEventArgs());
        await component.Find("input[aria-label='Filter value']").ChangeAsync(new ChangeEventArgs { Value = "bar" });
        await component.Find("button[aria-label='Cancel edit']").ClickAsync(new MouseEventArgs());

        await component.WaitForAssertionAsync(() =>
        {
            Assert.Empty(component.FindAll(".debug-log-filter-editor"));
            Assert.Equal("Message contains foo", component.Find(".debug-log-filter-summary").TextContent.Trim());
            Assert.Equal("1 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim());
        });
    }

    [Fact]
    public async Task DebugLogModal_CancelEditing_NewRow_RemovesIt()
    {
        _debugLogReader.LoadAsync(Arg.Any<CancellationToken>()).Returns(
            DebugLogUtils.ToAsyncEnumerable([DebugLogUtils.BuildLine(LogLevel.Information, "alpha foo")]));

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("1 of 1 entry", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        await AddFilterAsync(component);
        await component.Find("input[aria-label='Filter value']").ChangeAsync(new ChangeEventArgs { Value = "foo" });

        Assert.Single(component.FindAll(".debug-log-filter-editor"));

        await component.Find("button[aria-label='Cancel edit']").ClickAsync(new MouseEventArgs());

        await component.WaitForAssertionAsync(() => Assert.Empty(component.FindAll(".debug-log-filter-editor")));
        Assert.Empty(component.FindAll(".debug-log-filter-chip"));
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

        await SaveFilterAsync(component);

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

        await SaveFilterAsync(component);

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

        // Regression: with nothing selected the value header must be blank, not the "(Uncategorized)" sentinel
        // label, and nothing is applied yet so all entries remain visible.
        var valueHeader = component.Find("input[aria-label='Filter value']");
        Assert.True(
            string.IsNullOrEmpty(valueHeader.GetAttribute("value")),
            "Category value header must be blank when nothing is selected.");
        Assert.Equal("2 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim());

        await SelectOptionAsync(component, "Filter value", "(Uncategorized)");

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("(Uncategorized)", component.Find("input[aria-label='Filter value']").GetAttribute("value")));

        await SaveFilterAsync(component);

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("1 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));
    }

    [Fact]
    public async Task DebugLogModal_ChipExcludeToggle_AppliesImmediately()
    {
        var lines = new[]
        {
            DebugLogUtils.BuildLine(LogLevel.Information, "foo a"),
            DebugLogUtils.BuildLine(LogLevel.Information, "foo b"),
            DebugLogUtils.BuildLine(LogLevel.Information, "bar"),
        };

        _debugLogReader.LoadAsync(Arg.Any<CancellationToken>()).Returns(DebugLogUtils.ToAsyncEnumerable(lines));

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("3 of 3 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        await AddFilterAsync(component);
        await component.Find("input[aria-label='Filter value']").ChangeAsync(new ChangeEventArgs { Value = "foo" });
        await SaveFilterAsync(component);

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("2 of 3 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        // The chip exclude toggle acts on the applied filter, so it re-projects immediately.
        await component.Find("button[aria-label='Filter is included; activate to exclude']").ClickAsync(new MouseEventArgs());

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("1 of 3 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));
    }

    [Fact]
    public async Task DebugLogModal_ClearFilters_RemovesAllRowsAndShowsAllEntries()
    {
        var lines = new[]
        {
            DebugLogUtils.BuildLine(LogLevel.Information, "alpha foo"),
            DebugLogUtils.BuildLine(LogLevel.Error, "bravo"),
        };

        _debugLogReader.LoadAsync(Arg.Any<CancellationToken>()).Returns(DebugLogUtils.ToAsyncEnumerable(lines));

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("2 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        // Apply two filters (A: contains "foo", B: Level == Error) -> both narrow to 0 of 2 (no line is both).
        await AddFilterAsync(component);
        await component.Find("input[aria-label='Filter value']").ChangeAsync(new ChangeEventArgs { Value = "foo" });
        await SaveFilterAsync(component);

        await AddFilterAsync(component);
        await SelectOptionAsync(component, "Filter field", "Level");
        await SelectOptionAsync(component, "Filter value", "Error");
        await SaveFilterAsync(component);

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("0 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        await FindButton(component, "Clear Filters").ClickAsync(new MouseEventArgs());

        await component.WaitForAssertionAsync(() =>
        {
            Assert.Empty(component.FindAll(".debug-log-filter-chip"));
            Assert.Equal("2 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim());
        });
    }

    [Fact]
    public async Task DebugLogModal_ClearLog_ClearsTheLogFile()
    {
        _debugLogReader.LoadAsync(Arg.Any<CancellationToken>()).Returns(
            DebugLogUtils.ToAsyncEnumerable([DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogFirstMessage)]));

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("1 of 1 entry", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        await FindButton(component, "Clear Log").ClickAsync(new MouseEventArgs());

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
        await SaveFilterAsync(component);

        await component.WaitForAssertionAsync(() => Assert.Single(component.FindAll(".debug-log-filter-chip")));

        await component.Find("button[aria-label='Edit filter: Message contains foo']").ClickAsync(new MouseEventArgs());

        Assert.Single(component.FindAll(".debug-log-filter-editor"));
        Assert.Empty(component.FindAll(".debug-log-filter-chip"));
    }

    [Fact]
    public async Task DebugLogModal_EditingADisabledFilter_KeepsItDisabled()
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
        await SaveFilterAsync(component);

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("1 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        await component.Find("button[aria-label='Filter is enabled; activate to disable']").ClickAsync(new MouseEventArgs());

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("2 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        // Editing then saving a disabled filter must not silently re-enable it.
        await component.Find("button[aria-label='Edit filter: Message contains foo']").ClickAsync(new MouseEventArgs());
        await SaveFilterAsync(component);

        await component.WaitForAssertionAsync(() =>
        {
            Assert.Equal("2 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim());
            Assert.NotNull(component.Find("button[aria-label='Filter is disabled; activate to enable']"));
        });
    }

    [Fact]
    public async Task DebugLogModal_EnableToggle_DisablesFilterLive()
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
        await SaveFilterAsync(component);

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("1 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        // Disabling the chip drops it from the projection live; re-enabling restores it.
        await component.Find("button[aria-label='Filter is enabled; activate to disable']").ClickAsync(new MouseEventArgs());

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("2 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        await component.Find("button[aria-label='Filter is disabled; activate to enable']").ClickAsync(new MouseEventArgs());

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("1 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));
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

        await SaveFilterAsync(component);

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

        // Configuring the value does not change the projection until Save is clicked.
        Assert.Equal("2 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim());

        await SaveFilterAsync(component);

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("1 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));
    }

    [Fact]
    public async Task DebugLogModal_MultipleEditorsOpenSimultaneously()
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

        // Adding a second filter while the first is still being edited keeps both editors open (no block, no
        // implicit collapse); neither is applied until its own Save.
        await AddFilterAsync(component);
        await AddFilterAsync(component);

        Assert.Equal(2, component.FindAll(".debug-log-filter-editor").Count);
        Assert.Empty(component.FindAll(".debug-log-filter-chip"));
        Assert.Equal("2 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim());
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
        await SaveFilterAsync(component);

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("2 of 3 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        // Re-open the chip and switch Multi Select -> Equals; the stale second value is dropped so only Error
        // (the first value) remains after Save.
        await component.Find(".debug-log-filter-chip-edit").ClickAsync(new MouseEventArgs());
        await SelectOptionAsync(component, "Filter operator", "Equals");
        await SaveFilterAsync(component);

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

        await SaveFilterAsync(component);

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("1 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));
    }

    [Fact]
    public async Task DebugLogModal_RefreshWhileEditing_ProjectsAppliedNotEditedCopy()
    {
        var lines = new[]
        {
            DebugLogUtils.BuildLine(LogLevel.Information, "foo 1"),
            DebugLogUtils.BuildLine(LogLevel.Information, "foo 2"),
            DebugLogUtils.BuildLine(LogLevel.Information, "bar 1"),
        };

        _debugLogReader.LoadAsync(Arg.Any<CancellationToken>()).Returns(_ => DebugLogUtils.ToAsyncEnumerable(lines));

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("3 of 3 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        // Apply A: contains "foo" -> 2 of 3.
        await AddFilterAsync(component);
        await component.Find("input[aria-label='Filter value']").ChangeAsync(new ChangeEventArgs { Value = "foo" });
        await SaveFilterAsync(component);

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("2 of 3 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        // Re-open A and edit the copy to "bar" WITHOUT Save, then Refresh. Refresh must project A's applied "foo"
        // form (2 of 3), not the edited copy "bar" (which would be 1 of 3).
        await component.Find(".debug-log-filter-chip-edit").ClickAsync(new MouseEventArgs());
        await component.Find("input[aria-label='Filter value']").ChangeAsync(new ChangeEventArgs { Value = "bar" });
        await FindButton(component, "Refresh").ClickAsync(new MouseEventArgs());

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("2 of 3 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));
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
        await SaveFilterAsync(component);

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("1 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        // Removing the applied chip re-projects live.
        await component.Find(".debug-log-filter-chip-remove").ClickAsync(new MouseEventArgs());

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("2 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));
    }

    [Fact]
    public async Task DebugLogModal_SaveFilter_CollapsesToChipWithSummaryAndApplies()
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

        await SaveFilterAsync(component);

        await component.WaitForAssertionAsync(() => Assert.Single(component.FindAll(".debug-log-filter-chip")));
        Assert.Empty(component.FindAll(".debug-log-filter-editor"));
        Assert.Equal("Message contains foo", component.Find(".debug-log-filter-summary").TextContent.Trim());
        Assert.Equal("1 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim());
    }

    [Fact]
    public async Task DebugLogModal_SaveFilter_DisabledUntilComplete()
    {
        _debugLogReader.LoadAsync(Arg.Any<CancellationToken>()).Returns(
            DebugLogUtils.ToAsyncEnumerable([DebugLogUtils.BuildLine(LogLevel.Information, "alpha foo")]));

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("1 of 1 entry", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        await AddFilterAsync(component);

        Assert.True(component.Find("button[aria-label='Save filter']").HasAttribute("disabled"));

        await component.Find("input[aria-label='Filter value']").ChangeAsync(new ChangeEventArgs { Value = "foo" });

        Assert.False(component.Find("button[aria-label='Save filter']").HasAttribute("disabled"));
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

    private static async Task SaveFilterAsync(IRenderedComponent<DebugLogModal> component) =>
        await component.Find("button[aria-label='Save filter']").ClickAsync(new MouseEventArgs());

    private static async Task SelectOptionAsync(IRenderedComponent<DebugLogModal> component, string ariaLabel, string optionText)
    {
        var dropdown = component.FindAll(".dropdown-input")
            .First(element => element.QuerySelector($"input[aria-label='{ariaLabel}']") is not null);

        var option = dropdown.QuerySelectorAll("[role='option']")
            .First(item => item.TextContent.Trim() == optionText);

        await option.MouseDownAsync(new MouseEventArgs());
    }
}
