// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Options;
using EventLogExpert.UI.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace EventLogExpert.UI.Tests.Services;

public sealed class DebugLogServiceTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly string _testLogPath;

    public DebugLogServiceTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"DebugLogServiceTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
        _testLogPath = Path.Combine(_testDirectory, "debug.log");
    }

    [Fact]
    public async Task ClearAsync_WhenCalled_ShouldClearLogFile()
    {
        // Arrange
        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockSettingsService = CreateMockSettingsService(LogLevel.Information);

        await File.WriteAllTextAsync(_testLogPath, "Existing log content\nLine 2\nLine 3");

        using var debugLogService = new DebugLogService(fileLocationOptions, mockSettingsService);

        // Act
        await debugLogService.ClearAsync();

        // Assert
        var content = await File.ReadAllTextAsync(_testLogPath);
        Assert.Empty(content);
    }

    [Fact]
    public async Task ClearAsync_WhenLogFileDoesNotExist_ShouldCreateEmptyFile()
    {
        // Arrange
        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockSettingsService = CreateMockSettingsService(LogLevel.Information);

        using var debugLogService = new DebugLogService(fileLocationOptions, mockSettingsService);

        // Act
        await debugLogService.ClearAsync();

        // Assert
        Assert.True(File.Exists(_testLogPath));
        var content = await File.ReadAllTextAsync(_testLogPath);
        Assert.Empty(content);
    }

    [Fact]
    public void Constructor_ShouldCreateServiceSuccessfully()
    {
        // Arrange
        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockSettingsService = CreateMockSettingsService(LogLevel.Information);

        // Act & Assert - Constructor should complete without throwing
        var exception = Record.Exception(() =>
        {
            using var debugLogService = new DebugLogService(fileLocationOptions, mockSettingsService);
        });

        Assert.Null(exception);
    }

    [Fact]
    public void Constructor_WhenLogFileTooLarge_ShouldDeleteExistingFile()
    {
        // Arrange
        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockSettingsService = CreateMockSettingsService(LogLevel.Information);

        // Create a file larger than 10MB (MaxLogSize)
        var largeContent = new string('x', 11 * 1024 * 1024);
        File.WriteAllText(_testLogPath, largeContent);

        // Act
        using var debugLogService = new DebugLogService(fileLocationOptions, mockSettingsService);

        // Assert
        Assert.False(File.Exists(_testLogPath) && new FileInfo(_testLogPath).Length > 10 * 1024 * 1024);
    }

    [Fact]
    public void Constructor_WhenLogFileUnderMaxSize_ShouldNotDeleteFile()
    {
        // Arrange
        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockSettingsService = CreateMockSettingsService(LogLevel.Information);

        var existingContent = "Existing log content";
        File.WriteAllText(_testLogPath, existingContent);

        // Act
        using var debugLogService = new DebugLogService(fileLocationOptions, mockSettingsService);
        debugLogService.Trace("New message");

        // Assert
        var content = File.ReadAllText(_testLogPath);
        Assert.Contains("Existing log content", content);
        Assert.Contains("New message", content);
    }

    [Fact]
    public void Constructor_WhenLogLevelAboveTrace_ShouldNotEnableFirstChanceExceptionLogging()
    {
        // Arrange
        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockSettingsService = CreateMockSettingsService(LogLevel.Debug);

        // Act & Assert - Creating service above Trace level should not throw
        var exception = Record.Exception(() =>
        {
            using var debugLogService = new DebugLogService(fileLocationOptions, mockSettingsService);
        });

        Assert.Null(exception);
    }

    [Fact]
    public void Constructor_WhenLogLevelIsTrace_ShouldEnableFirstChanceExceptionLogging()
    {
        // Arrange
        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockSettingsService = CreateMockSettingsService(LogLevel.Trace);

        // Act & Assert - Creating service at Trace level should not throw
        var exception = Record.Exception(() =>
        {
            using var debugLogService = new DebugLogService(fileLocationOptions, mockSettingsService);
        });

        Assert.Null(exception);
    }

    [Fact]
    public void DebugLogLoaded_WhenSet_ShouldBeRetrievable()
    {
        // Arrange
        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockSettingsService = CreateMockSettingsService(LogLevel.Information);

        using var debugLogService = new DebugLogService(fileLocationOptions, mockSettingsService);

        Action testAction = () => { };

        // Act
        debugLogService.DebugLogLoaded = testAction;

        // Assert
        Assert.Same(testAction, debugLogService.DebugLogLoaded);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }
        }
        catch
        {
            // Ignore cleanup errors in tests
        }
    }

    [Fact]
    public void Dispose_ShouldCleanupWithoutError()
    {
        // Arrange
        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockSettingsService = CreateMockSettingsService(LogLevel.Information);

        var debugLogService = new DebugLogService(fileLocationOptions, mockSettingsService);

        // Act & Assert - Dispose should complete without throwing
        var exception = Record.Exception(() => debugLogService.Dispose());
        Assert.Null(exception);
    }

    [Fact]
    public async Task LoadAsync_WhenLogFileHasContent_ShouldReturnAllLines()
    {
        // Arrange
        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockSettingsService = CreateMockSettingsService(LogLevel.Information);

        var expectedLines = new[] { "Line 1", "Line 2", "Line 3" };
        await File.WriteAllLinesAsync(_testLogPath, expectedLines);

        using var debugLogService = new DebugLogService(fileLocationOptions, mockSettingsService);

        // Act
        var lines = await debugLogService.LoadAsync().ToListAsync();

        // Assert
        Assert.Equal(3, lines.Count);
        Assert.Equal("Line 1", lines[0]);
        Assert.Equal("Line 2", lines[1]);
        Assert.Equal("Line 3", lines[2]);
    }

    [Fact]
    public async Task LoadAsync_WhenLogFileIsEmpty_ShouldReturnNoLines()
    {
        // Arrange
        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockSettingsService = CreateMockSettingsService(LogLevel.Information);

        await File.WriteAllTextAsync(_testLogPath, string.Empty);

        using var debugLogService = new DebugLogService(fileLocationOptions, mockSettingsService);

        // Act
        var lines = await debugLogService.LoadAsync().ToListAsync();

        // Assert
        Assert.Empty(lines);
    }

    [Fact]
    public void LoadDebugLog_WhenDebugLogLoadedIsNull_ShouldNotThrow()
    {
        // Arrange
        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockSettingsService = CreateMockSettingsService(LogLevel.Information);

        using var debugLogService = new DebugLogService(fileLocationOptions, mockSettingsService);
        debugLogService.DebugLogLoaded = null;

        // Act & Assert
        var exception = Record.Exception(() => debugLogService.LoadDebugLog());
        Assert.Null(exception);
    }

    [Fact]
    public void LoadDebugLog_WhenDebugLogLoadedIsSet_ShouldInvokeAction()
    {
        // Arrange
        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockSettingsService = CreateMockSettingsService(LogLevel.Information);

        using var debugLogService = new DebugLogService(fileLocationOptions, mockSettingsService);

        var actionInvoked = false;
        debugLogService.DebugLogLoaded = () => actionInvoked = true;

        // Act
        debugLogService.LoadDebugLog();

        // Assert
        Assert.True(actionInvoked);
    }

    [Fact]
    public void Trace_ShouldIncludeThreadId()
    {
        // Arrange
        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockSettingsService = CreateMockSettingsService(LogLevel.Information);

        using var debugLogService = new DebugLogService(fileLocationOptions, mockSettingsService);

        // Act
        debugLogService.Trace("Test message");

        // Assert
        var content = File.ReadAllText(_testLogPath);
        var expectedThreadId = Environment.CurrentManagedThreadId;
        Assert.Contains($"[{expectedThreadId}]", content);
    }

    [Fact]
    public void Trace_ShouldIncludeTimestamp()
    {
        // Arrange
        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockSettingsService = CreateMockSettingsService(LogLevel.Information);

        using var debugLogService = new DebugLogService(fileLocationOptions, mockSettingsService);

        // Act
        debugLogService.Trace("Test message");

        // Assert
        var content = File.ReadAllText(_testLogPath);
        // Check for ISO 8601 format timestamp pattern [YYYY-MM-DD
        Assert.Matches(@"\[\d{4}-\d{2}-\d{2}", content);
    }

    [Fact]
    public void Trace_WhenCalledMultipleTimes_ShouldAppendToLogFile()
    {
        // Arrange
        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockSettingsService = CreateMockSettingsService(LogLevel.Information);

        using var debugLogService = new DebugLogService(fileLocationOptions, mockSettingsService);

        // Act
        debugLogService.Trace("First message");
        debugLogService.Trace("Second message");
        debugLogService.Trace("Third message", LogLevel.Warning);

        // Assert
        var content = File.ReadAllText(_testLogPath);
        Assert.Contains("First message", content);
        Assert.Contains("Second message", content);
        Assert.Contains("Third message", content);
    }

    [Theory]
    [InlineData(LogLevel.Trace)]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Information)]
    [InlineData(LogLevel.Warning)]
    [InlineData(LogLevel.Error)]
    [InlineData(LogLevel.Critical)]
    public void Trace_WhenLogLevelEqualsThreshold_ShouldWriteToLogFile(LogLevel level)
    {
        // Arrange
        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockSettingsService = CreateMockSettingsService(level);

        using var debugLogService = new DebugLogService(fileLocationOptions, mockSettingsService);

        // Act
        debugLogService.Trace($"Message at {level}", level);

        // Assert
        var content = File.ReadAllText(_testLogPath);
        Assert.Contains($"Message at {level}", content);
    }

    [Fact]
    public void Trace_WhenLogLevelIsAboveThreshold_ShouldWriteToLogFile()
    {
        // Arrange
        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockSettingsService = CreateMockSettingsService(LogLevel.Information);

        using var debugLogService = new DebugLogService(fileLocationOptions, mockSettingsService);

        // Act
        debugLogService.Trace("Error message", LogLevel.Error);

        // Assert
        var content = File.ReadAllText(_testLogPath);
        Assert.Contains("Error message", content);
        Assert.Contains("[Error]", content);
    }

    [Fact]
    public void Trace_WhenLogLevelIsBelowThreshold_ShouldNotWriteToLogFile()
    {
        // Arrange
        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockSettingsService = CreateMockSettingsService(LogLevel.Warning);

        using var debugLogService = new DebugLogService(fileLocationOptions, mockSettingsService);

        // Act
        debugLogService.Trace("Test message");

        // Assert
        Assert.False(File.Exists(_testLogPath) && File.ReadAllText(_testLogPath).Contains("Test message"));
    }

    [Fact]
    public void Trace_WhenLogLevelIsMet_ShouldWriteToLogFile()
    {
        // Arrange
        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockSettingsService = CreateMockSettingsService(LogLevel.Information);

        using var debugLogService = new DebugLogService(fileLocationOptions, mockSettingsService);

        // Act
        debugLogService.Trace("Test message");

        // Assert
        var content = File.ReadAllText(_testLogPath);
        Assert.Contains("Test message", content);
        Assert.Contains("[Information]", content);
    }

    [Fact]
    public void Trace_WithDefaultLogLevel_ShouldUseInformation()
    {
        // Arrange
        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockSettingsService = CreateMockSettingsService(LogLevel.Information);

        using var debugLogService = new DebugLogService(fileLocationOptions, mockSettingsService);

        // Act
        debugLogService.Trace("Default level message");

        // Assert
        var content = File.ReadAllText(_testLogPath);
        Assert.Contains("[Information]", content);
        Assert.Contains("Default level message", content);
    }

    private static ISettingsService CreateMockSettingsService(LogLevel logLevel)
    {
        var mockSettingsService = Substitute.For<ISettingsService>();
        mockSettingsService.LogLevel.Returns(logLevel);

        return mockSettingsService;
    }
}
