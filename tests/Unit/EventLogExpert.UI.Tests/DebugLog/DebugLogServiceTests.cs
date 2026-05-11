// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.DebugLog;
using EventLogExpert.UI.Options;
using EventLogExpert.UI.Settings;
using EventLogExpert.UI.Tests.TestUtils.Constants;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace EventLogExpert.UI.Tests.DebugLog;

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

        await File.WriteAllTextAsync(_testLogPath,
            $"{Constants.DebugLogExistingContent}\n{Constants.DebugLogLine2}\n{Constants.DebugLogLine3}",
            TestContext.Current.CancellationToken);

        using var debugLogService = new DebugLogService(fileLocationOptions, mockSettingsService);

        // Act
        await debugLogService.ClearAsync();

        // Assert
        var content = await File.ReadAllTextAsync(_testLogPath, TestContext.Current.CancellationToken);
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
        var content = await File.ReadAllTextAsync(_testLogPath, TestContext.Current.CancellationToken);
        Assert.Empty(content);
    }

    [Fact]
    public async Task ClearAsync_WhenSecondInstanceHoldsWriter_ShouldNotThrowFileLockException()
    {
        // Arrange
        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockSettingsService = CreateMockSettingsService(LogLevel.Information);

        using var firstInstance = new DebugLogService(fileLocationOptions, mockSettingsService);
        firstInstance.Information($"first instance line");

        using var secondInstance = new DebugLogService(fileLocationOptions, mockSettingsService);
        secondInstance.Information($"second instance line");

        // Act + Assert - clearing from one instance must not fail because the other holds the writer.
        var exception = await Record.ExceptionAsync(() => firstInstance.ClearAsync());
        Assert.Null(exception);

        // Both instances must write cleanly post-clear; no NUL padding from stale positions.
        secondInstance.Information($"after clear from second");
        firstInstance.Information($"after clear from first");

        var bytes = ReadLogFileBytes();
        Assert.DoesNotContain((byte)0, bytes);

        var content = ReadLogFile();
        var lines = content.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Contains("after clear from second", lines[0]);
        Assert.Contains("after clear from first", lines[1]);
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

        // Create a file larger than 10MB (MaxLogSize) without allocating a large string
        using (var fs = new FileStream(_testLogPath, FileMode.Create, FileAccess.Write))
        {
            fs.SetLength(11 * 1024 * 1024);
        }

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

        File.WriteAllText(_testLogPath, Constants.DebugLogExistingContent);

        // Act
        using var debugLogService = new DebugLogService(fileLocationOptions, mockSettingsService);
        debugLogService.Information($"{Constants.DebugLogNewMessage}");

        // Assert
        var content = ReadLogFile();
        Assert.Contains(Constants.DebugLogExistingContent, content);
        Assert.Contains(Constants.DebugLogNewMessage, content);
    }

    [Fact]
    public void Constructor_WhenLogLevelAboveTrace_ShouldNotThrow()
    {
        // Arrange
        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockSettingsService = CreateMockSettingsService(LogLevel.Debug);

        // Act & Assert
        var exception = Record.Exception(() =>
        {
            using var debugLogService = new DebugLogService(fileLocationOptions, mockSettingsService);
        });

        Assert.Null(exception);
    }

    [Fact]
    public void Constructor_WhenLogLevelIsTrace_ShouldNotThrow()
    {
        // Arrange
        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockSettingsService = CreateMockSettingsService(LogLevel.Trace);

        // Act & Assert
        var exception = Record.Exception(() =>
        {
            using var debugLogService = new DebugLogService(fileLocationOptions, mockSettingsService);
        });

        Assert.Null(exception);
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
        var exception = Record.Exception(debugLogService.Dispose);
        Assert.Null(exception);
    }

    [Fact]
    public async Task LoadAsync_WhenLogFileDeletedDuringRead_ShouldAllowDeletion()
    {
        // Arrange
        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockSettingsService = CreateMockSettingsService(LogLevel.Information);

        var expectedLines = new[] { Constants.DebugLogLine1, Constants.DebugLogLine2, Constants.DebugLogLine3 };
        await File.WriteAllLinesAsync(_testLogPath, expectedLines, TestContext.Current.CancellationToken);

        using var debugLogService = new DebugLogService(fileLocationOptions, mockSettingsService);

        // Act - Start enumeration and delete the file while the reader holds a handle
        await using var enumerator =
            debugLogService.LoadAsync(TestContext.Current.CancellationToken)
                .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        // Read first line to ensure the file is open
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(Constants.DebugLogLine1, enumerator.Current);

        // Delete should succeed because FileShare.Delete is set
        var deleteException = Record.Exception(() => File.Delete(_testLogPath));

        // Assert - Deletion should not throw
        Assert.Null(deleteException);

        // Continue reading remaining lines (file content is still accessible via open handle)
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(Constants.DebugLogLine2, enumerator.Current);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(Constants.DebugLogLine3, enumerator.Current);

        Assert.False(await enumerator.MoveNextAsync());
    }

    [Fact]
    public async Task LoadAsync_WhenLogFileDoesNotExist_ShouldReturnNoLines()
    {
        // Arrange
        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockSettingsService = CreateMockSettingsService(LogLevel.Information);

        // Ensure the log file does not exist (fresh install scenario)
        if (File.Exists(_testLogPath))
        {
            File.Delete(_testLogPath);
        }

        using var debugLogService = new DebugLogService(fileLocationOptions, mockSettingsService);

        // Act
        var lines = await debugLogService.LoadAsync(TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(lines);
    }

    [Fact]
    public async Task LoadAsync_WhenLogFileHasContent_ShouldReturnAllLines()
    {
        // Arrange
        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockSettingsService = CreateMockSettingsService(LogLevel.Information);

        var expectedLines = new[] { Constants.DebugLogLine1, Constants.DebugLogLine2, Constants.DebugLogLine3 };
        await File.WriteAllLinesAsync(_testLogPath, expectedLines, TestContext.Current.CancellationToken);

        using var debugLogService = new DebugLogService(fileLocationOptions, mockSettingsService);

        // Act
        var lines = await debugLogService.LoadAsync(TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, lines.Count);
        Assert.Equal(Constants.DebugLogLine1, lines[0]);
        Assert.Equal(Constants.DebugLogLine2, lines[1]);
        Assert.Equal(Constants.DebugLogLine3, lines[2]);
    }

    [Fact]
    public async Task LoadAsync_WhenLogFileIsEmpty_ShouldReturnNoLines()
    {
        // Arrange
        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockSettingsService = CreateMockSettingsService(LogLevel.Information);

        await File.WriteAllTextAsync(_testLogPath, string.Empty, TestContext.Current.CancellationToken);

        using var debugLogService = new DebugLogService(fileLocationOptions, mockSettingsService);

        // Act
        var lines = await debugLogService.LoadAsync(TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(lines);
    }

    [Fact]
    public void MinimumLevel_WhenLogLevelChangedAtRuntime_ShouldReflectNewLevel()
    {
        // Arrange
        var (mockSettingsService, setLogLevel) = CreateMockSettingsServiceWithDynamicLogLevel(LogLevel.Information);

        using var debugLogService = new DebugLogService(
            new FileLocationOptions(_testDirectory),
            mockSettingsService);

        // Assert initial
        Assert.Equal(LogLevel.Information, debugLogService.MinimumLevel);

        // Act - change level
        setLogLevel(LogLevel.Warning);

        // Assert updated
        Assert.Equal(LogLevel.Warning, debugLogService.MinimumLevel);
    }

    [Fact]
    public void TraceIfEnabled_WhenLogLevelChangedAtRuntime_ShouldRespectNewLevel()
    {
        // Arrange
        var (mockSettingsService, setLogLevel) = CreateMockSettingsServiceWithDynamicLogLevel(LogLevel.Debug);

        using var debugLogService = new DebugLogService(
            new FileLocationOptions(_testDirectory),
            mockSettingsService);

        // Act - write at Debug (should succeed)
        debugLogService.Debug($"debug message before change");
        var contentBefore = ReadLogFile();
        Assert.Contains("debug message before change", contentBefore);

        // Change level to Warning
        setLogLevel(LogLevel.Warning);

        // Write at Debug (should be filtered by handler)
        debugLogService.Debug($"debug message after change");

        // Write at Warning (should succeed)
        debugLogService.Warning($"warning message after change");

        // Assert
        var content = ReadLogFile();
        Assert.Contains("debug message before change", content);
        Assert.DoesNotContain("debug message after change", content);
        Assert.Contains("warning message after change", content);
    }

    [Fact]
    public void Trace_ShouldIncludeThreadId()
    {
        // Arrange
        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockSettingsService = CreateMockSettingsService(LogLevel.Information);

        using var debugLogService = new DebugLogService(fileLocationOptions, mockSettingsService);

        // Act
        debugLogService.Information($"{Constants.DebugLogTestMessage}");

        // Assert
        var content = ReadLogFile();
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
        debugLogService.Information($"{Constants.DebugLogTestMessage}");

        // Assert
        var content = ReadLogFile();
        // Check for ISO 8601 format timestamp pattern [YYYY-MM-DD
        Assert.Matches(@"\[\d{4}-\d{2}-\d{2}", content);
    }

    [Fact]
    public async Task Trace_WhenCalledFromMultipleThreadsConcurrently_ShouldProduceOrderedAndCompleteOutput()
    {
        // Arrange — exercises the writer's synchronization boundary: timestamp capture, prefix formatting,
        // and the underlying StreamWriter call all happen INSIDE _writeLock.EnterScope(). A regression that
        // moves any of those steps back outside the lock will surface as either (a) interleaved/garbled
        // lines that fail to parse, (b) timestamps that go backwards within the file, or (c) lost or
        // duplicated payloads.
        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockSettingsService = CreateMockSettingsService(LogLevel.Information);
        const int WriterCount = 64;
        const int WritesPerWriter = 4;
        const int TotalWrites = WriterCount * WritesPerWriter;
        var startGate = new TaskCompletionSource();

        // Act
        using (var debugLogService = new DebugLogService(fileLocationOptions, mockSettingsService))
        {
            var writers = Enumerable.Range(0, WriterCount)
                .Select(writerIndex => Task.Run(async () =>
                {
                    await startGate.Task;

                    for (var writeIndex = 0; writeIndex < WritesPerWriter; writeIndex++)
                    {
                        debugLogService.Information($"writer-{writerIndex}-msg-{writeIndex}");
                    }
                }, TestContext.Current.CancellationToken))
                .ToArray();

            startGate.SetResult();
            await Task.WhenAll(writers);
        }

        // Assert
        var lines = await File.ReadAllLinesAsync(_testLogPath, TestContext.Current.CancellationToken);

        Assert.Equal(TotalWrites, lines.Length);

        var timestamps = new List<DateTimeOffset>(lines.Length);
        var payloads = new List<string>(lines.Length);

        foreach (var line in lines)
        {
            // Use the production parser directly so the test enforces the actual producer/consumer
            // contract — if the prefix shape changes in DebugLogService.WriteTrace, the parser's
            // regex changes alongside it, and this assertion follows automatically.
            Assert.True(
                DebugLogEntryParser.TryParseLine(line, out var entry),
                $"Line did not parse via DebugLogEntryParser: {line}");

            timestamps.Add(entry.Timestamp!.Value);
            payloads.Add(entry.Message);
        }

        for (var i = 1; i < timestamps.Count; i++)
        {
            // <= because DateTime.Now resolution can produce equal timestamps for closely-spaced
            // captures inside the lock; what we forbid is a regression (later timestamp earlier in file).
            Assert.True(
                timestamps[i] >= timestamps[i - 1],
                $"Timestamp regressed at line {i}: {timestamps[i - 1]:o} -> {timestamps[i]:o}");
        }

        var expectedPayloads = Enumerable.Range(0, WriterCount)
            .SelectMany(writerIndex => Enumerable.Range(0, WritesPerWriter)
                .Select(writeIndex => $"writer-{writerIndex}-msg-{writeIndex}"))
            .ToHashSet();

        // Equality on HashSet asserts both "no missing payloads" and "no duplicated/extra payloads".
        Assert.Equal(expectedPayloads, payloads.ToHashSet());
        Assert.Equal(TotalWrites, payloads.Count);
    }

    [Fact]
    public void Trace_WhenCalledMultipleTimes_ShouldAppendToLogFile()
    {
        // Arrange
        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockSettingsService = CreateMockSettingsService(LogLevel.Information);

        using var debugLogService = new DebugLogService(fileLocationOptions, mockSettingsService);

        // Act
        debugLogService.Information($"{Constants.DebugLogFirstMessage}");
        debugLogService.Information($"{Constants.DebugLogSecondMessage}");
        debugLogService.Information($"{Constants.DebugLogThirdMessage}");

        // Assert
        var content = ReadLogFile();
        Assert.Contains(Constants.DebugLogFirstMessage, content);
        Assert.Contains(Constants.DebugLogSecondMessage, content);
        Assert.Contains(Constants.DebugLogThirdMessage, content);
    }

    [Fact]
    public void Trace_WhenLogLevelChangedAtRuntime_ShouldRespectNewLevel()
    {
        // Arrange
        var (mockSettingsService, setLogLevel) = CreateMockSettingsServiceWithDynamicLogLevel(LogLevel.Information);

        using var debugLogService = new DebugLogService(
            new FileLocationOptions(_testDirectory),
            mockSettingsService);

        // Act - write at Information (should succeed)
        debugLogService.Information($"{Constants.DebugLogFirstMessage}");

        // Change level to Warning at runtime
        setLogLevel(LogLevel.Warning);

        // Write at Information (should be filtered now)
        debugLogService.Information($"{Constants.DebugLogSecondMessage}");

        // Write at Warning (should succeed)
        debugLogService.Warning($"{Constants.DebugLogThirdMessage}");

        // Assert
        var content = ReadLogFile();
        Assert.Contains(Constants.DebugLogFirstMessage, content);
        Assert.DoesNotContain(Constants.DebugLogSecondMessage, content);
        Assert.Contains(Constants.DebugLogThirdMessage, content);
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
        TraceAtLevel(debugLogService, $"Message at {level}", level);

        // Assert
        var content = ReadLogFile();
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
        debugLogService.Error($"{Constants.DebugLogErrorMessage}");

        // Assert
        var content = ReadLogFile();
        Assert.Contains(Constants.DebugLogErrorMessage, content);
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
        debugLogService.Information($"{Constants.DebugLogTestMessage}");

        // Assert
        Assert.False(File.Exists(_testLogPath) && ReadLogFile().Contains(Constants.DebugLogTestMessage));
    }

    [Fact]
    public void Trace_WhenLogLevelIsMet_ShouldWriteToLogFile()
    {
        // Arrange
        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockSettingsService = CreateMockSettingsService(LogLevel.Information);

        using var debugLogService = new DebugLogService(fileLocationOptions, mockSettingsService);

        // Act
        debugLogService.Information($"{Constants.DebugLogTestMessage}");

        // Assert
        var content = ReadLogFile();
        Assert.Contains(Constants.DebugLogTestMessage, content);
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
        debugLogService.Information($"{Constants.DebugLogDefaultLevelMessage}");

        // Assert
        var content = ReadLogFile();
        Assert.Contains("[Information]", content);
        Assert.Contains(Constants.DebugLogDefaultLevelMessage, content);
    }

    [Fact]
    public void WriteTrace_WhenSecondInstanceWritesToSameFile_ShouldNotThrowFileLockException()
    {
        // Arrange
        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockSettingsService = CreateMockSettingsService(LogLevel.Information);

        using var firstInstance = new DebugLogService(fileLocationOptions, mockSettingsService);
        firstInstance.Information($"first instance line");

        using var secondInstance = new DebugLogService(fileLocationOptions, mockSettingsService);

        // Act + Assert
        var exception = Record.Exception(() => secondInstance.Information($"second instance line"));
        Assert.Null(exception);
    }

    private static ISettingsService CreateMockSettingsService(LogLevel logLevel)
    {
        var mockSettingsService = Substitute.For<ISettingsService>();
        mockSettingsService.LogLevel.Returns(logLevel);

        return mockSettingsService;
    }

    // Mock settings service that supports dynamic log level changes via LogLevelChanged.
    private static (ISettingsService service, Action<LogLevel> setLogLevel)
        CreateMockSettingsServiceWithDynamicLogLevel(LogLevel initialLevel)
    {
        var currentLevel = initialLevel;
        var mockSettingsService = Substitute.For<ISettingsService>();
        mockSettingsService.LogLevel.Returns(_ => currentLevel);

        void SetLogLevel(LogLevel newLevel)
        {
            currentLevel = newLevel;
            mockSettingsService.LogLevelChanged?.Invoke();
        }

        return (mockSettingsService, SetLogLevel);
    }

    private static void TraceAtLevel(DebugLogService service, string message, LogLevel level)
    {
        switch (level)
        {
            case LogLevel.Trace: service.Trace($"{message}"); break;
            case LogLevel.Debug: service.Debug($"{message}"); break;
            case LogLevel.Information: service.Information($"{message}"); break;
            case LogLevel.Warning: service.Warning($"{message}"); break;
            case LogLevel.Error: service.Error($"{message}"); break;
            case LogLevel.Critical: service.Critical($"{message}"); break;
            default: throw new ArgumentOutOfRangeException(nameof(level));
        }
    }

    private string ReadLogFile()
    {
        using var stream = new FileStream(
            _testLogPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    private byte[] ReadLogFileBytes()
    {
        using var stream = new FileStream(
            _testLogPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        using var ms = new MemoryStream();
        stream.CopyTo(ms);

        return ms.ToArray();
    }
}
