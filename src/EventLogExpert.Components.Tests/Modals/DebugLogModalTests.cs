// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Components.Modals;
using EventLogExpert.Components.Tests.TestUtils;
using EventLogExpert.Components.Tests.TestUtils.Constants;
using EventLogExpert.UI.Interfaces;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Text;

namespace EventLogExpert.Components.Tests.Modals;

public sealed class DebugLogModalTests : BunitContext
{
    private readonly IAlertDialogService _alertDialogService = Substitute.For<IAlertDialogService>();
    private readonly IClipboardService _clipboardService = Substitute.For<IClipboardService>();
    private readonly IFileLogger _fileLogger = Substitute.For<IFileLogger>();
    private readonly IFileSaveService _fileSaveService = Substitute.For<IFileSaveService>();
    private readonly IModalService _modalService = Substitute.For<IModalService>();

    public DebugLogModalTests()
    {
        _modalService.ActiveModalId.Returns(1L);

        Services.AddSingleton(_alertDialogService);
        Services.AddSingleton(_clipboardService);
        Services.AddSingleton(_fileLogger);
        Services.AddSingleton(_fileSaveService);
        Services.AddSingleton(_modalService);
        Services.AddFluxor(options => options.ScanAssemblies(typeof(DebugLogModal).Assembly));

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public async Task DebugLogModal_AfterLoad_FooterCounterIsPoliteLiveStatusRegion()
    {
        // Arrange
        var lines = new[] { DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogFirstMessage) };
        _fileLogger.LoadAsync(Arg.Any<CancellationToken>()).Returns(DebugLogUtils.ToAsyncEnumerable(lines));

        // Act
        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("1 of 1 entry", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        // Assert
        var counter = component.Find(".debug-log-footer-counter");
        Assert.Equal("status", counter.GetAttribute("role"));
        Assert.Equal("polite", counter.GetAttribute("aria-live"));
        Assert.Equal("true", counter.GetAttribute("aria-atomic"));
    }

    [Fact]
    public async Task DebugLogModal_AfterLoad_ViewportHasRegionRoleWithoutAriaLiveAndIsKeyboardFocusable()
    {
        // Arrange
        var lines = new[] { DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogFirstMessage) };
        _fileLogger.LoadAsync(Arg.Any<CancellationToken>()).Returns(DebugLogUtils.ToAsyncEnumerable(lines));

        // Act
        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("1 of 1 entry", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        // Assert
        var viewport = component.Find(".debug-log-viewport");
        Assert.Equal("region", viewport.GetAttribute("role"));
        Assert.Equal("0", viewport.GetAttribute("tabindex"));
        Assert.Null(viewport.GetAttribute("aria-live"));
        Assert.Equal("Debug log entries", viewport.GetAttribute("aria-label"));
    }

    [Fact]
    public async Task DebugLogModal_AfterLoad_VirtualizeItemsAreNewestFirstAndPreserveContinuationLineOrder()
    {
        // Arrange
        var firstHeader = DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogFirstMessage);
        var secondHeader = DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogSecondMessage);
        const string ContinuationLine = "  at MyMethod()";
        var thirdHeader = DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogThirdMessage);

        _fileLogger.LoadAsync(Arg.Any<CancellationToken>()).Returns(
            DebugLogUtils.ToAsyncEnumerable([firstHeader, secondHeader, ContinuationLine, thirdHeader]));

        // Act
        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("3 of 3 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        // Assert
        var rows = component.FindAll(".debug-log-row");
        Assert.Equal(
            new[] { thirdHeader, secondHeader, ContinuationLine, firstHeader },
            rows.Select(row => row.TextContent).ToArray());
    }

    [Fact]
    public async Task DebugLogModal_ClearDuringStreamingLoad_StaleStreamDoesNotMutateState()
    {
        // Arrange
        var gate = new TaskCompletionSource();

        async IAsyncEnumerable<string> Source()
        {
            for (var i = 0; i < 100; i++)
            {
                yield return DebugLogUtils.BuildLine(LogLevel.Information, $"msg-{i}");
            }

            await gate.Task;

            for (var i = 100; i < 130; i++)
            {
                yield return DebugLogUtils.BuildLine(LogLevel.Information, $"msg-{i}");
            }
        }

        _fileLogger.LoadAsync(Arg.Any<CancellationToken>()).Returns(Source());
        _fileLogger.ClearAsync().Returns(Task.CompletedTask);

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(
            () => Assert.Equal("99 of 99 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()),
            TimeSpan.FromSeconds(2));

        // Act
        await component.Find("button:contains('Clear')").ClickAsync(new());

        await component.WaitForAssertionAsync(
            () => Assert.Equal("0 of 0 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()),
            TimeSpan.FromSeconds(2));

        gate.SetResult();

        await Task.Delay(200, Xunit.TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("0 of 0 entries", component.Find(".debug-log-footer-counter").TextContent.Trim());
    }

    [Fact]
    public async Task DebugLogModal_ClearSucceeds_ResetsEntriesAndCounter()
    {
        // Arrange
        var lines = new[]
        {
            DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogFirstMessage),
            DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogSecondMessage),
        };

        _fileLogger.LoadAsync(Arg.Any<CancellationToken>()).Returns(DebugLogUtils.ToAsyncEnumerable(lines));
        _fileLogger.ClearAsync().Returns(Task.CompletedTask);

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("2 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        // Act
        await component.Find("button:contains('Clear')").ClickAsync(new());

        // Assert
        await _fileLogger.Received(1).ClearAsync();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("0 of 0 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        await _alertDialogService.DidNotReceive().ShowAlert(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task DebugLogModal_ClearThrows_PreservesEntriesAndShowsAlert()
    {
        // Arrange
        var lines = new[]
        {
            DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogFirstMessage),
            DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogSecondMessage),
        };

        _fileLogger.LoadAsync(Arg.Any<CancellationToken>()).Returns(DebugLogUtils.ToAsyncEnumerable(lines));
        _fileLogger.ClearAsync().ThrowsAsync(new InvalidOperationException("permission denied"));

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("2 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        // Act
        await component.Find("button:contains('Clear')").ClickAsync(new());

        // Assert: alert shown and counter unchanged (UI state preserved on failure)
        await _alertDialogService.Received(1).ShowAlert("Clear Failed", "permission denied", "OK");

        Assert.Equal("2 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim());
    }

    [Fact]
    public async Task DebugLogModal_CopyClick_CallsCopyTextAsyncWithEnvironmentNewLineJoinedDisplayed()
    {
        // Arrange
        var lines = new[]
        {
            DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogFirstMessage),
            DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogSecondMessage),
        };

        _fileLogger.LoadAsync(Arg.Any<CancellationToken>()).Returns(DebugLogUtils.ToAsyncEnumerable(lines));

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("2 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        var expectedContent = string.Join(Environment.NewLine, new[] { lines[1], lines[0] });

        // Act
        await component.Find("button:contains('Copy')").ClickAsync(new());

        // Assert
        await _clipboardService.Received(1).CopyTextAsync(expectedContent);
    }

    [Fact]
    public async Task DebugLogModal_CopyClickWhilePendingFilterPending_FlushesFilterBeforeReadingDisplayed()
    {
        // Arrange
        var lines = new[]
        {
            DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogFirstMessage),
            DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogErrorMessage),
            DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogSecondMessage),
        };

        _fileLogger.LoadAsync(Arg.Any<CancellationToken>()).Returns(DebugLogUtils.ToAsyncEnumerable(lines));

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("3 of 3 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        // Act — typed text doesn't apply until the 250ms debounce; Copy must flush first.
        await component.Find("input[aria-label='Filter messages']").InputAsync(new() { Value = "error" });
        await component.Find("button:contains('Copy')").ClickAsync(new());

        // Assert: clipboard receives only the entry whose Message contains "error".
        await _clipboardService.Received(1).CopyTextAsync(lines[1]);
        await _clipboardService.DidNotReceive().CopyTextAsync(
            Arg.Is<string>(s => s.Contains(Constants.DebugLogFirstMessage)));
    }

    [Fact]
    public async Task DebugLogModal_DuringLoad_RendersFirstBatchBeforeStreamCompletes()
    {
        // Arrange — gate stops the stream at 100 lines so the modal must paint the in-flight batch.
        var gate = new TaskCompletionSource();

        async IAsyncEnumerable<string> Source()
        {
            for (var i = 0; i < 100; i++)
            {
                yield return DebugLogUtils.BuildLine(LogLevel.Information, $"msg-{i}");
            }

            await gate.Task;

            for (var i = 100; i < 130; i++)
            {
                yield return DebugLogUtils.BuildLine(LogLevel.Information, $"msg-{i}");
            }
        }

        _fileLogger.LoadAsync(Arg.Any<CancellationToken>()).Returns(Source());

        // Act
        var component = Render<DebugLogModal>();

        // Assert — streaming parser buffers one entry pending; 100 lines surface as 99.
        await component.WaitForAssertionAsync(
            () => Assert.Equal("99 of 99 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()),
            TimeSpan.FromSeconds(2));

        gate.SetResult();

        await component.WaitForAssertionAsync(
            () => Assert.Equal("130 of 130 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()),
            TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task DebugLogModal_EmptyLog_CopyButtonIsDisabled()
    {
        // Arrange
        _fileLogger.LoadAsync(Arg.Any<CancellationToken>()).Returns(DebugLogUtils.ToAsyncEnumerable([]));

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("0 of 0 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        // Act + Assert
        var copyButton = component.Find("button:contains('Copy')");

        Assert.True(copyButton.HasAttribute("disabled"));
    }

    [Fact]
    public async Task DebugLogModal_EmptyLog_ExportButtonIsDisabled()
    {
        // Arrange
        _fileLogger.LoadAsync(Arg.Any<CancellationToken>()).Returns(DebugLogUtils.ToAsyncEnumerable([]));

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("0 of 0 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        // Act + Assert
        var exportButton = component.Find("button:contains('Export')");

        Assert.True(exportButton.HasAttribute("disabled"));
    }

    [Fact]
    public async Task DebugLogModal_EmptyLog_ShowsLogIsEmptyMessageAndZeroCounter()
    {
        // Arrange
        _fileLogger.LoadAsync(Arg.Any<CancellationToken>()).Returns(DebugLogUtils.ToAsyncEnumerable([]));

        // Act
        var component = Render<DebugLogModal>();

        // Assert
        await component.WaitForAssertionAsync(() =>
            Assert.Equal("0 of 0 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        Assert.Contains("Log is Empty...", component.Markup);
    }

    [Fact]
    public async Task DebugLogModal_EmptyLogWithActiveFilter_StillShowsLogIsEmptyMessage()
    {
        // Arrange
        _fileLogger.LoadAsync(Arg.Any<CancellationToken>()).Returns(DebugLogUtils.ToAsyncEnumerable([]));

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("0 of 0 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        // Act: type a filter while the log is empty
        await component.Find("input[aria-label='Filter messages']").InputAsync(new() { Value = "any-search-text" });

        // Assert — zero entries (not zero filtered); "No entries match filters" would mislead.
        await component.WaitForAssertionAsync(() =>
        {
            Assert.Contains("Log is Empty...", component.Markup);
            Assert.DoesNotContain("No entries match filters.", component.Markup);
        });
    }

    [Fact]
    public async Task DebugLogModal_ExportClick_CallsSaveAsyncWithEnvironmentNewLineJoinedDisplayed()
    {
        // Arrange
        var lines = new[]
        {
            DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogFirstMessage),
            DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogSecondMessage),
        };

        _fileLogger.LoadAsync(Arg.Any<CancellationToken>()).Returns(DebugLogUtils.ToAsyncEnumerable(lines));

        Func<Stream, Task>? capturedWriter = null;

        _fileSaveService.SaveAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                Arg.Do<Func<Stream, Task>>(writer => capturedWriter = writer))
            .Returns(Task.FromResult<string?>("C:\\debug-log.log"));

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("2 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        var expectedContent = string.Join(Environment.NewLine, new[] { lines[1], lines[0] });

        // Act
        await component.Find("button:contains('Export')").ClickAsync(new());

        // Assert
        await _fileSaveService.Received(1).SaveAsync(
            Arg.Is<string>(name => name.StartsWith("debug-log-") && name.EndsWith(".log")),
            FileSaveServiceFileTypes.Log,
            Arg.Any<Func<Stream, Task>>());

        Assert.NotNull(capturedWriter);
        Assert.Equal(expectedContent, await InvokeWriterAndDecodeAsync(capturedWriter));
    }

    [Fact]
    public async Task DebugLogModal_ExportClickWhilePendingFilterPending_FlushesFilterBeforeReadingDisplayed()
    {
        // Arrange
        var lines = new[]
        {
            DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogFirstMessage),
            DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogErrorMessage),
            DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogSecondMessage),
        };

        _fileLogger.LoadAsync(Arg.Any<CancellationToken>()).Returns(DebugLogUtils.ToAsyncEnumerable(lines));

        Func<Stream, Task>? capturedWriter = null;

        _fileSaveService.SaveAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                Arg.Do<Func<Stream, Task>>(writer => capturedWriter = writer))
            .Returns(Task.FromResult<string?>("C:\\debug-log.log"));

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("3 of 3 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        // Act: type a filter that drops two entries, then click Export before debounce expires.
        await component.Find("input[aria-label='Filter messages']").InputAsync(new() { Value = "error" });
        await component.Find("button:contains('Export')").ClickAsync(new());

        // Assert
        await _fileSaveService.Received(1).SaveAsync(
            Arg.Any<string>(),
            FileSaveServiceFileTypes.Log,
            Arg.Any<Func<Stream, Task>>());

        Assert.NotNull(capturedWriter);
        Assert.Equal(lines[1], await InvokeWriterAndDecodeAsync(capturedWriter));
    }

    [Fact]
    public async Task DebugLogModal_ExportReturnsNull_DoesNotShowAlert()
    {
        // Arrange
        var lines = new[] { DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogTestMessage) };

        _fileLogger.LoadAsync(Arg.Any<CancellationToken>()).Returns(DebugLogUtils.ToAsyncEnumerable(lines));
        _fileSaveService.SaveAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                Arg.Any<Func<Stream, Task>>())
            .Returns(Task.FromResult<string?>(null));

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("1 of 1 entry", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        // Act
        await component.Find("button:contains('Export')").ClickAsync(new());

        // Assert
        await _alertDialogService.DidNotReceive().ShowAlert(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task DebugLogModal_ExportThrows_ShowsExportFailedAlert()
    {
        // Arrange
        var lines = new[] { DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogTestMessage) };

        _fileLogger.LoadAsync(Arg.Any<CancellationToken>()).Returns(DebugLogUtils.ToAsyncEnumerable(lines));
        _fileSaveService.SaveAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                Arg.Any<Func<Stream, Task>>())
            .ThrowsAsync(new InvalidOperationException("disk full"));

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("1 of 1 entry", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        // Act
        await component.Find("button:contains('Export')").ClickAsync(new());

        // Assert
        await _alertDialogService.Received(1).ShowAlert("Export Failed", "disk full", "OK");
    }

    [Fact]
    public async Task DebugLogModal_FilterMatchesNoEntries_ShowsNoEntriesMatchFiltersMessage()
    {
        // Arrange
        var lines = new[]
        {
            DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogFirstMessage),
            DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogSecondMessage),
        };

        _fileLogger.LoadAsync(Arg.Any<CancellationToken>()).Returns(DebugLogUtils.ToAsyncEnumerable(lines));

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("2 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        // Act
        await component.Find("input[aria-label='Filter messages']").InputAsync(new() { Value = "no-such-text" });

        // Assert
        await component.WaitForAssertionAsync(
            () => Assert.Equal("0 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()),
            TimeSpan.FromSeconds(2));

        Assert.Contains("No entries match filters.", component.Markup);
    }

    [Fact]
    public async Task DebugLogModal_LevelDropdownInitial_AllOptionShowsAriaSelectedTrueAndSpecificLevelsDoNot()
    {
        // Arrange
        _fileLogger.LoadAsync(Arg.Any<CancellationToken>()).Returns(DebugLogUtils.ToAsyncEnumerable([]));

        // Act
        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("0 of 0 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        // Assert
        var allOption = component.FindAll("[role='option']").Single(o => o.TextContent.Trim() == "All");
        Assert.Equal("true", allOption.GetAttribute("aria-selected"));

        var traceOption = component.FindAll("[role='option']").Single(o => o.TextContent.Trim() == nameof(LogLevel.Trace));
        Assert.Equal("false", traceOption.GetAttribute("aria-selected"));
    }

    [Fact]
    public async Task DebugLogModal_LevelFilterChangedMidStream_RemainingBatchesUseNewFilter()
    {
        // Arrange
        var gate = new TaskCompletionSource();

        async IAsyncEnumerable<string> Source()
        {
            for (var i = 0; i < 100; i++)
            {
                yield return DebugLogUtils.BuildLine(LogLevel.Information, $"info-{i}");
            }

            await gate.Task;

            for (var i = 100; i < 130; i++)
            {
                yield return DebugLogUtils.BuildLine(LogLevel.Error, $"error-{i}");
            }
        }

        _fileLogger.LoadAsync(Arg.Any<CancellationToken>()).Returns(Source());

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(
            () => Assert.Equal("99 of 99 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()),
            TimeSpan.FromSeconds(2));

        // Act
        var levelDropdown = component.Find("input[aria-label='Level']").ParentElement!;
        var errorOption = levelDropdown.QuerySelectorAll("[role='option']")
            .First(o => o.TextContent.Trim() == "Error");

        await errorOption.MouseDownAsync(new());

        await component.WaitForAssertionAsync(
            () => Assert.Equal("0 of 99 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()),
            TimeSpan.FromSeconds(2));

        gate.SetResult();

        // Assert
        await component.WaitForAssertionAsync(
            () => Assert.Equal("30 of 130 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()),
            TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task DebugLogModal_LevelFilterChangedWhilePendingFilterPending_FlushesPendingTextFilter()
    {
        // Arrange
        var lines = new[]
        {
            DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogFirstMessage),
            DebugLogUtils.BuildLine(LogLevel.Information, "matching info"),
            DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogSecondMessage),
            DebugLogUtils.BuildLine(LogLevel.Error, "matching error"),
            DebugLogUtils.BuildLine(LogLevel.Error, Constants.DebugLogThirdMessage),
        };

        _fileLogger.LoadAsync(Arg.Any<CancellationToken>()).Returns(DebugLogUtils.ToAsyncEnumerable(lines));

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("5 of 5 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        // Act — type a string filter (debounce starts), then change the level dropdown before it elapses.
        await component.Find("input[aria-label='Filter messages']").InputAsync(new() { Value = "matching" });

        var levelDropdown = component.Find("input[aria-label='Level']").ParentElement!;
        var errorOption = levelDropdown.QuerySelectorAll("[role='option']")
            .First(o => o.TextContent.Trim() == "Error");

        await errorOption.MouseDownAsync(new());

        // Assert — only the entry that matches BOTH filters survives.
        await component.WaitForAssertionAsync(() =>
            Assert.Equal("1 of 5 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));
    }

    [Fact]
    public async Task DebugLogModal_LevelFilterEqualsError_NarrowsToOneEntry()
    {
        // Arrange
        var lines = new[]
        {
            DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogFirstMessage),
            DebugLogUtils.BuildLine(LogLevel.Error, Constants.DebugLogErrorMessage),
            DebugLogUtils.BuildLine(LogLevel.Warning, Constants.DebugLogSecondMessage),
        };

        _fileLogger.LoadAsync(Arg.Any<CancellationToken>()).Returns(DebugLogUtils.ToAsyncEnumerable(lines));

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("3 of 3 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        // Act: click "Error" in the level value dropdown
        var levelDropdown = component.Find("input[aria-label='Level']").ParentElement!;
        var errorOption = levelDropdown.QuerySelectorAll("[role='option']")
            .First(o => o.TextContent.Trim() == "Error");

        await errorOption.MouseDownAsync(new());

        // Assert
        await component.WaitForAssertionAsync(() =>
            Assert.Equal("1 of 3 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));
    }

    [Fact]
    public async Task DebugLogModal_LevelFilterMultiSelectErrorAndWarning_KeepsBoth()
    {
        // Arrange
        var lines = new[]
        {
            DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogFirstMessage),
            DebugLogUtils.BuildLine(LogLevel.Error, Constants.DebugLogErrorMessage),
            DebugLogUtils.BuildLine(LogLevel.Warning, Constants.DebugLogSecondMessage),
        };

        _fileLogger.LoadAsync(Arg.Any<CancellationToken>()).Returns(DebugLogUtils.ToAsyncEnumerable(lines));

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("3 of 3 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        // Act: switch operator to "Multi Select"
        var operatorDropdown = component.Find("input[aria-label='Level operator']").ParentElement!;
        var multiSelectOption = operatorDropdown.QuerySelectorAll("[role='option']")
            .First(o => o.TextContent.Trim() == "Multi Select");

        await multiSelectOption.MouseDownAsync(new());

        // Then pick Error and Warning from the multi-select dropdown
        var levelsDropdown = component.Find("input[aria-label='Levels']").ParentElement!;

        var errorOption = levelsDropdown.QuerySelectorAll("[role='option']")
            .First(o => o.TextContent.Trim() == "Error");

        await errorOption.MouseDownAsync(new());

        var warningOption = levelsDropdown.QuerySelectorAll("[role='option']")
            .First(o => o.TextContent.Trim() == "Warning");

        await warningOption.MouseDownAsync(new());

        // Assert: Error and Warning remain; Information is filtered out
        await component.WaitForAssertionAsync(() =>
            Assert.Equal("2 of 3 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));
    }

    [Fact]
    public async Task DebugLogModal_LevelFilterNotEqualInformation_HidesInformationEntries()
    {
        // Arrange
        var lines = new[]
        {
            DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogFirstMessage),
            DebugLogUtils.BuildLine(LogLevel.Error, Constants.DebugLogErrorMessage),
            DebugLogUtils.BuildLine(LogLevel.Warning, Constants.DebugLogSecondMessage),
        };

        _fileLogger.LoadAsync(Arg.Any<CancellationToken>()).Returns(DebugLogUtils.ToAsyncEnumerable(lines));

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("3 of 3 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        // Act: switch operator to "Not Equal"
        var operatorDropdown = component.Find("input[aria-label='Level operator']").ParentElement!;
        var notEqualOption = operatorDropdown.QuerySelectorAll("[role='option']")
            .First(o => o.TextContent.Trim() == "Not Equal");

        await notEqualOption.MouseDownAsync(new());

        // Then select "Information" as the value to exclude
        var levelDropdown = component.Find("input[aria-label='Level']").ParentElement!;
        var informationOption = levelDropdown.QuerySelectorAll("[role='option']")
            .First(o => o.TextContent.Trim() == "Information");

        await informationOption.MouseDownAsync(new());

        // Assert: Error and Warning remain (Information is the only level excluded)
        await component.WaitForAssertionAsync(() =>
            Assert.Equal("2 of 3 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));
    }

    [Fact]
    public async Task DebugLogModal_LoadLifecycle_ViewportAriaBusyTogglesFromTrueToFalse()
    {
        // Arrange
        var loadGate = new TaskCompletionSource();

        async IAsyncEnumerable<string> Source()
        {
            await loadGate.Task;
            yield return DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogFirstMessage);
        }

        _fileLogger.LoadAsync(Arg.Any<CancellationToken>()).Returns(Source());

        // Act
        var component = Render<DebugLogModal>();

        // Assert: in-flight (aria-busy=true)
        await component.WaitForAssertionAsync(() =>
            Assert.Equal("true", component.Find(".debug-log-viewport").GetAttribute("aria-busy")));

        loadGate.SetResult();

        // Assert: settled (aria-busy=false)
        await component.WaitForAssertionAsync(() =>
            Assert.Equal("false", component.Find(".debug-log-viewport").GetAttribute("aria-busy")));
    }

    [Fact]
    public async Task DebugLogModal_PopulatedLog_CounterShowsTotalEntries()
    {
        // Arrange
        var lines = new[]
        {
            DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogFirstMessage),
            DebugLogUtils.BuildLine(LogLevel.Error, Constants.DebugLogErrorMessage),
            DebugLogUtils.BuildLine(LogLevel.Warning, Constants.DebugLogSecondMessage),
        };

        _fileLogger.LoadAsync(Arg.Any<CancellationToken>()).Returns(DebugLogUtils.ToAsyncEnumerable(lines));

        // Act
        var component = Render<DebugLogModal>();

        // Assert
        await component.WaitForAssertionAsync(() =>
            Assert.Equal("3 of 3 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));
    }

    [Fact]
    public async Task DebugLogModal_RefreshClickedWhilePendingFilterPending_UsesNewFilterForReloadedEntries()
    {
        // Arrange
        var lines = new[]
        {
            DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogFirstMessage),
            DebugLogUtils.BuildLine(LogLevel.Information, "matching"),
            DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogSecondMessage),
        };

        _fileLogger.LoadAsync(Arg.Any<CancellationToken>()).Returns(DebugLogUtils.ToAsyncEnumerable(lines));

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("3 of 3 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        // Act — type a filter (debounce starts), then click Refresh before it elapses.
        await component.Find("input[aria-label='Filter messages']").InputAsync(new() { Value = "matching" });
        await component.Find("button:contains('Refresh')").ClickAsync(new());

        // Assert — the reload re-projects with the just-typed filter, not the prior empty one.
        await component.WaitForAssertionAsync(() =>
            Assert.Equal("1 of 3 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));
    }

    [Fact]
    public async Task DebugLogModal_RefreshLoadAsyncThrows_ProjectsPartialDataAndShowsAlert()
    {
        // Arrange
        var lines = new[]
        {
            DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogFirstMessage),
            DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogSecondMessage),
        };

        _fileLogger.LoadAsync(Arg.Any<CancellationToken>()).Returns(
            DebugLogUtils.YieldThenThrow(lines, new InvalidOperationException("read failed")));

        // Act
        var component = Render<DebugLogModal>();

        // Assert: partial entries projected and alert shown (no rethrow to caller)
        await component.WaitForAssertionAsync(() =>
            Assert.Equal("2 of 2 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        await component.WaitForAssertionAsync(() =>
            _alertDialogService.Received(1).ShowAlert("Refresh Failed", "read failed", "OK"));
    }

    [Fact]
    public async Task DebugLogModal_StringFilterAfterDebounce_NarrowsToMatchingEntries()
    {
        // Arrange
        var lines = new[]
        {
            DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogFirstMessage),
            DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogErrorMessage),
            DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogSecondMessage),
        };

        _fileLogger.LoadAsync(Arg.Any<CancellationToken>()).Returns(DebugLogUtils.ToAsyncEnumerable(lines));

        var component = Render<DebugLogModal>();

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("3 of 3 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        // Act
        await component.Find("input[aria-label='Filter messages']").InputAsync(new() { Value = "error" });

        // Assert (wait long enough for the 250ms debounce + projection)
        await component.WaitForAssertionAsync(
            () => Assert.Equal("1 of 3 entries", component.Find(".debug-log-footer-counter").TextContent.Trim()),
            TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task DebugLogModal_WhenRefreshIsRestartedWhileLoading_CancelsInFlightLoad()
    {
        // Arrange
        var firstStartedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObservedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var fastLines = new[] { DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogFirstMessage) };

        _fileLogger.LoadAsync(Arg.Any<CancellationToken>())
            .Returns(
                call => HangingSource(call.Arg<CancellationToken>(), firstStartedTcs, cancellationObservedTcs),
                _ => DebugLogUtils.ToAsyncEnumerable(fastLines));

        var component = Render<DebugLogModal>();

        await firstStartedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), Xunit.TestContext.Current.CancellationToken);

        // Act - clicking Refresh starts a new load, which cancels the in-flight one
        await component.Find(".debug-log-footer-right button:first-child").ClickAsync(new());

        // Assert
        await cancellationObservedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), Xunit.TestContext.Current.CancellationToken);

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("1 of 1 entry", component.Find(".debug-log-footer-counter").TextContent.Trim()));
    }

    [Fact]
    public async Task DebugLogModal_WhenStaleProviderEmitsAfterRestart_DoesNotMutateDisplayedState()
    {
        // Arrange
        var firstStartedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStaleEmissionTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var staleDisposedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var staleResumedAfterYieldCount = 0;

        var fastLines = new[] { DebugLogUtils.BuildLine(LogLevel.Information, Constants.DebugLogFirstMessage) };

        _fileLogger.LoadAsync(Arg.Any<CancellationToken>())
            .Returns(
                _ => NonCooperativeSource(
                    firstStartedTcs,
                    releaseStaleEmissionTcs,
                    staleDisposedTcs,
                    () => Interlocked.Increment(ref staleResumedAfterYieldCount)),
                _ => DebugLogUtils.ToAsyncEnumerable(fastLines));

        var component = Render<DebugLogModal>();

        await firstStartedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), Xunit.TestContext.Current.CancellationToken);

        await component.Find(".debug-log-footer-right button:first-child").ClickAsync(new());

        await component.WaitForAssertionAsync(() =>
            Assert.Equal("1 of 1 entry", component.Find(".debug-log-footer-counter").TextContent.Trim()));

        // Release stale provider; guard should dispose after next yield (signalled via finally).
        releaseStaleEmissionTcs.TrySetResult();

        await staleDisposedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), Xunit.TestContext.Current.CancellationToken);

        // Counter unchanged; orphaned stale Refresh cannot mutate visible state.
        Assert.Equal("1 of 1 entry", component.Find(".debug-log-footer-counter").TextContent.Trim());

        // Guard rejected first post-release yield; flood not consumed.
        Assert.Equal(1, staleResumedAfterYieldCount);
    }

    private static async IAsyncEnumerable<string> HangingSource(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken,
        TaskCompletionSource startedTcs,
        TaskCompletionSource cancelledTcs)
    {
        yield return DebugLogUtils.BuildLine(LogLevel.Information, "first");
        startedTcs.TrySetResult();

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            cancelledTcs.TrySetResult();
            throw;
        }
    }

    private static async Task<string> InvokeWriterAndDecodeAsync(Func<Stream, Task> writer)
    {
        using var stream = new MemoryStream();

        await writer(stream);

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static async IAsyncEnumerable<string> NonCooperativeSource(
        TaskCompletionSource startedTcs,
        TaskCompletionSource releaseTcs,
        TaskCompletionSource disposedTcs,
        Action onResumedAfterYield)
    {
        try
        {
            yield return DebugLogUtils.BuildLine(LogLevel.Information, "stale-first");
            onResumedAfterYield();
            startedTcs.TrySetResult();

            // Deliberately ignore cancellation by awaiting a token-less wait.
            await releaseTcs.Task;

            // Flood >RenderBatchSize so projection would run absent the generation guard.
            for (var i = 0; i < 150; i++)
            {
                yield return DebugLogUtils.BuildLine(LogLevel.Information, $"stale-flood-{i}");
                onResumedAfterYield();
            }
        }
        finally
        {
            disposedTcs.TrySetResult();
        }
    }
}
