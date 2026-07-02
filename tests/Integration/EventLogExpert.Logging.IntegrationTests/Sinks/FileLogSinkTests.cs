// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Configuration;
using EventLogExpert.Logging.Routing;
using EventLogExpert.Logging.Sinks;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Logging.IntegrationTests.Sinks;

public sealed class FileLogSinkTests : IDisposable
{
    private const string DebugLogDefaultLevelMessage = "Default level message";
    private const string DebugLogErrorMessage = "Error message";
    private const string DebugLogExistingContent = "Existing log content";
    private const string DebugLogFirstMessage = "First message";
    private const string DebugLogLine2 = "Line 2";
    private const string DebugLogLine3 = "Line 3";
    private const string DebugLogNewMessage = "New message";
    private const string DebugLogSecondMessage = "Second message";
    private const string DebugLogTestMessage = "Test message";
    private const string DebugLogThirdMessage = "Third message";

    private readonly string _testDirectory;
    private readonly string _testLogPath;

    public FileLogSinkTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"FileLogSinkTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
        _testLogPath = Path.Combine(_testDirectory, "debug.log");
    }

    [Fact]
    public async Task ClearAsync_WhenCalled_ShouldClearLogFile()
    {
        await File.WriteAllTextAsync(_testLogPath,
            $"{DebugLogExistingContent}\n{DebugLogLine2}\n{DebugLogLine3}",
            TestContext.Current.CancellationToken);

        using FileLogSink fileSink = CreateFileSink(LogLevel.Information);

        await fileSink.ClearAsync();

        string content = await File.ReadAllTextAsync(_testLogPath, TestContext.Current.CancellationToken);
        Assert.Empty(content);
    }

    [Fact]
    public async Task ClearAsync_WhenLogFileDoesNotExist_ShouldCreateEmptyFile()
    {
        using FileLogSink fileSink = CreateFileSink(LogLevel.Information);

        await fileSink.ClearAsync();

        Assert.True(File.Exists(_testLogPath));
        string content = await File.ReadAllTextAsync(_testLogPath, TestContext.Current.CancellationToken);
        Assert.Empty(content);
    }

    [Fact]
    public async Task ClearAsync_WhenSecondInstanceHoldsWriter_ShouldNotThrowFileLockException()
    {
        using FileLogSink firstInstance = CreateFileSink(LogLevel.Information);
        firstInstance.Information("first instance line");

        using FileLogSink secondInstance = CreateFileSink(LogLevel.Information);
        secondInstance.Information("second instance line");

        Exception? exception = await Record.ExceptionAsync(() => firstInstance.ClearAsync());
        Assert.Null(exception);

        secondInstance.Information("after clear from second");
        firstInstance.Information("after clear from first");

        byte[] bytes = ReadLogFileBytes();
        Assert.DoesNotContain((byte)0, bytes);

        string[] lines = ReadLogFile().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Contains("after clear from second", lines[0]);
        Assert.Contains("after clear from first", lines[1]);
    }

    [Fact]
    public void Constructor_ShouldCreateServiceSuccessfully()
    {
        Exception? exception = Record.Exception(() =>
        {
            using FileLogSink fileSink = CreateFileSink(LogLevel.Information);
        });

        Assert.Null(exception);
    }

    [Fact]
    public void Constructor_WhenLogFileTooLarge_ShouldDeleteExistingFile()
    {
        using (FileStream fileStream = new(_testLogPath, FileMode.Create, FileAccess.Write))
        {
            fileStream.SetLength(11 * 1024 * 1024);
        }

        using FileLogSink fileSink = CreateFileSink(LogLevel.Information);

        Assert.False(File.Exists(_testLogPath) && new FileInfo(_testLogPath).Length > 10 * 1024 * 1024);
    }

    [Fact]
    public void Constructor_WhenLogFileUnderMaxSize_ShouldNotDeleteFile()
    {
        File.WriteAllText(_testLogPath, DebugLogExistingContent);

        using FileLogSink fileSink = CreateFileSink(LogLevel.Information);
        fileSink.Information(DebugLogNewMessage);

        string content = ReadLogFile();
        Assert.Contains(DebugLogExistingContent, content);
        Assert.Contains(DebugLogNewMessage, content);
    }

    [Fact]
    public void Constructor_WhenLogLevelAboveTrace_ShouldNotThrow()
    {
        Exception? exception = Record.Exception(() =>
        {
            using FileLogSink fileSink = CreateFileSink(LogLevel.Debug);
        });

        Assert.Null(exception);
    }

    [Fact]
    public void Constructor_WhenLogLevelIsTrace_ShouldNotThrow()
    {
        Exception? exception = Record.Exception(() =>
        {
            using FileLogSink fileSink = CreateFileSink(LogLevel.Trace);
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
        }
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        FileLogSink fileSink = CreateFileSink(LogLevel.Information);

        fileSink.Dispose();

        Assert.Null(Record.Exception(fileSink.Dispose));
    }

    [Fact]
    public void Dispose_ShouldCleanupWithoutError()
    {
        FileLogSink fileSink = CreateFileSink(LogLevel.Information);

        Exception? exception = Record.Exception(fileSink.Dispose);
        Assert.Null(exception);
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_DoesNotThrow()
    {
        FileLogSink fileSink = CreateFileSink(LogLevel.Information);

        await fileSink.DisposeAsync();

        Exception? exception = await Record.ExceptionAsync(async () => await fileSink.DisposeAsync());
        Assert.Null(exception);
    }

    [Fact]
    public async Task DisposeAsync_FollowedByDispose_DoesNotThrow()
    {
        FileLogSink fileSink = CreateFileSink(LogLevel.Information);

        await fileSink.DisposeAsync();

        Assert.Null(Record.Exception(fileSink.Dispose));
    }

    [Fact]
    public async Task DisposeAsync_ShouldCleanupWithoutError()
    {
        FileLogSink fileSink = CreateFileSink(LogLevel.Information);
        fileSink.Information("before dispose");

        Exception? exception = await Record.ExceptionAsync(async () => await fileSink.DisposeAsync());

        Assert.Null(exception);
        Assert.Null(Record.Exception(() => File.Delete(_testLogPath)));
    }

    [Fact]
    public async Task Emit_WhenCalledFromMultipleThreadsConcurrently_ShouldProduceCompleteNonInterleavedOutput()
    {
        const int WriterCount = 64;
        const int WritesPerWriter = 4;
        const int TotalWrites = WriterCount * WritesPerWriter;
        TaskCompletionSource startGate = new();

        using (FileLogSink fileSink = CreateFileSink(LogLevel.Information))
        {
            Task[] writers = Enumerable.Range(0, WriterCount)
                .Select(writerIndex => Task.Run(async () =>
                {
                    await startGate.Task;

                    for (int writeIndex = 0; writeIndex < WritesPerWriter; writeIndex++)
                    {
                        fileSink.Information($"writer-{writerIndex}-msg-{writeIndex}");
                    }
                }, TestContext.Current.CancellationToken))
                .ToArray();

            startGate.SetResult();
            await Task.WhenAll(writers);
        }

        string[] lines = await File.ReadAllLinesAsync(_testLogPath, TestContext.Current.CancellationToken);

        Assert.Equal(TotalWrites, lines.Length);
        Assert.All(lines, line => Assert.Matches(@"^\[[A-Za-z]+\]  writer-\d+-msg-\d+$", line));

        HashSet<string> payloads = [.. lines.Select(ExtractMessage)];
        HashSet<string> expectedPayloads = [.. Enumerable.Range(0, WriterCount)
            .SelectMany(writerIndex => Enumerable.Range(0, WritesPerWriter)
                .Select(writeIndex => $"writer-{writerIndex}-msg-{writeIndex}"))];

        Assert.Equal(expectedPayloads, payloads);
        Assert.Equal(TotalWrites, payloads.Count);
    }

    [Fact]
    public void Emit_WhenCalledMultipleTimes_ShouldAppendToLogFile()
    {
        using FileLogSink fileSink = CreateFileSink(LogLevel.Information);

        fileSink.Information(DebugLogFirstMessage);
        fileSink.Information(DebugLogSecondMessage);
        fileSink.Information(DebugLogThirdMessage);

        string content = ReadLogFile();
        Assert.Contains(DebugLogFirstMessage, content);
        Assert.Contains(DebugLogSecondMessage, content);
        Assert.Contains(DebugLogThirdMessage, content);
    }

    [Fact]
    public void Emit_WhenGlobalBaselineUpdatedAtRuntime_ShouldRespectNewLevel()
    {
        LogRoutingPolicy routingPolicy = CreateRoutingPolicy(LogLevel.Information);
        using FileLogSink fileSink = new(_testLogPath, routingPolicy, Format);

        fileSink.Information(DebugLogFirstMessage);
        routingPolicy.UpdateGlobalBaseline(LogLevel.Warning);
        fileSink.Information(DebugLogSecondMessage);
        fileSink.Warning(DebugLogThirdMessage);

        string content = ReadLogFile();
        Assert.Contains(DebugLogFirstMessage, content);
        Assert.DoesNotContain(DebugLogSecondMessage, content);
        Assert.Contains(DebugLogThirdMessage, content);
    }

    [Theory]
    [InlineData(LogLevel.Trace)]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Information)]
    [InlineData(LogLevel.Warning)]
    [InlineData(LogLevel.Error)]
    [InlineData(LogLevel.Critical)]
    public void Emit_WhenLogLevelEqualsThreshold_ShouldWriteToLogFile(LogLevel level)
    {
        using FileLogSink fileSink = CreateFileSink(level);

        EmitAtLevel(fileSink, $"Message at {level}", level);

        string content = ReadLogFile();
        Assert.Contains($"Message at {level}", content);
    }

    [Fact]
    public void Emit_WhenLogLevelIsAboveThreshold_ShouldWriteToLogFile()
    {
        using FileLogSink fileSink = CreateFileSink(LogLevel.Information);

        fileSink.Error(DebugLogErrorMessage);

        string content = ReadLogFile();
        Assert.Contains(DebugLogErrorMessage, content);
        Assert.Contains("[Error]", content);
    }

    [Fact]
    public void Emit_WhenLogLevelIsBelowThreshold_ShouldNotWriteToLogFile()
    {
        using FileLogSink fileSink = CreateFileSink(LogLevel.Warning);

        fileSink.Information(DebugLogTestMessage);

        Assert.False(File.Exists(_testLogPath) && ReadLogFile().Contains(DebugLogTestMessage));
    }

    [Fact]
    public void Emit_WhenLogLevelIsMet_ShouldWriteToLogFile()
    {
        using FileLogSink fileSink = CreateFileSink(LogLevel.Information);

        fileSink.Information(DebugLogTestMessage);

        string content = ReadLogFile();
        Assert.Contains(DebugLogTestMessage, content);
        Assert.Contains("[Information]", content);
    }

    [Fact]
    public void Emit_WithDefaultLogLevel_ShouldUseInformation()
    {
        using FileLogSink fileSink = CreateFileSink(LogLevel.Information);

        fileSink.Information(DebugLogDefaultLevelMessage);

        string content = ReadLogFile();
        Assert.Contains("[Information]", content);
        Assert.Contains(DebugLogDefaultLevelMessage, content);
    }

    [Fact]
    public void MinimumLevelFor_WhenGlobalBaselineUpdatedAtRuntime_ShouldReflectNewLevel()
    {
        LogRoutingPolicy routingPolicy = CreateRoutingPolicy(LogLevel.Information);
        using FileLogSink fileSink = new(_testLogPath, routingPolicy, Format);

        Assert.Equal(LogLevel.Information, fileSink.MinimumLevelFor(LogSourceFactory.DefaultCategory));

        routingPolicy.UpdateGlobalBaseline(LogLevel.Warning);

        Assert.Equal(LogLevel.Warning, fileSink.MinimumLevelFor(LogSourceFactory.DefaultCategory));
    }

    [Fact]
    public void WriteTrace_WhenSecondInstanceWritesToSameFile_ShouldNotThrowFileLockException()
    {
        using FileLogSink firstInstance = CreateFileSink(LogLevel.Information);
        firstInstance.Information("first instance line");

        using FileLogSink secondInstance = CreateFileSink(LogLevel.Information);

        Exception? exception = Record.Exception(() => secondInstance.Information("second instance line"));
        Assert.Null(exception);

        string content = ReadLogFile();
        Assert.Contains("first instance line", content);
        Assert.Contains("second instance line", content);
    }

    private static LogRoutingPolicy CreateRoutingPolicy(LogLevel logLevel) =>
        new(LoggingOptions.CreateShippedDefaults(), logLevel);

    private static void EmitAtLevel(FileLogSink fileSink, string message, LogLevel level)
    {
        switch (level)
        {
            case LogLevel.Trace: fileSink.Trace(message); break;
            case LogLevel.Debug: fileSink.Debug(message); break;
            case LogLevel.Information: fileSink.Information(message); break;
            case LogLevel.Warning: fileSink.Warning(message); break;
            case LogLevel.Error: fileSink.Error(message); break;
            case LogLevel.Critical: fileSink.Critical(message); break;
            default: throw new ArgumentOutOfRangeException(nameof(level));
        }
    }

    private static string ExtractMessage(string line)
    {
        int secondSeparator = line.IndexOf("]  ", StringComparison.Ordinal);
        return line[(secondSeparator + 3)..];
    }

    private static string Format(LogRecord record) =>
        $"[{record.Level}] {record.Category} {record.Message}";

    private FileLogSink CreateFileSink(LogLevel logLevel) =>
        new(_testLogPath, CreateRoutingPolicy(logLevel), Format);

    private string ReadLogFile()
    {
        using FileStream stream = new(
            _testLogPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        using StreamReader reader = new(stream);

        return reader.ReadToEnd();
    }

    private byte[] ReadLogFileBytes()
    {
        using FileStream stream = new(
            _testLogPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        using MemoryStream memoryStream = new();
        stream.CopyTo(memoryStream);

        return memoryStream.ToArray();
    }
}

static file class FileLogSinkTestLogging
{
    public static void Critical(this FileLogSink sink, string message) => sink.Emit(new LogRecord(DateTime.UtcNow, LogLevel.Critical, message));

    public static void Debug(this FileLogSink sink, string message) => sink.Emit(new LogRecord(DateTime.UtcNow, LogLevel.Debug, message));

    public static void Error(this FileLogSink sink, string message) => sink.Emit(new LogRecord(DateTime.UtcNow, LogLevel.Error, message));

    public static void Information(this FileLogSink sink, string message) => sink.Emit(new LogRecord(DateTime.UtcNow, LogLevel.Information, message));

    public static void Trace(this FileLogSink sink, string message) => sink.Emit(new LogRecord(DateTime.UtcNow, LogLevel.Trace, message));

    public static void Warning(this FileLogSink sink, string message) => sink.Emit(new LogRecord(DateTime.UtcNow, LogLevel.Warning, message));
}
