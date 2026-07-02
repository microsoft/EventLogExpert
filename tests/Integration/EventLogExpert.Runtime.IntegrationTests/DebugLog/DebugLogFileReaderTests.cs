// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Configuration;
using EventLogExpert.Logging.Routing;
using EventLogExpert.Logging.Sinks;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.DebugLog;
using EventLogExpert.Runtime.IntegrationTests.TestUtils.Constants;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Runtime.IntegrationTests.DebugLog;

public sealed class DebugLogFileReaderTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly string _testLogPath;

    public DebugLogFileReaderTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"DebugLogFileReaderTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
        _testLogPath = Path.Combine(_testDirectory, "debug.log");
    }

    [Fact]
    public async Task ClearAsync_WhenCalled_ShouldDelegateToFileSink()
    {
        using FileLogSink fileSink = CreateFileSink();
        DebugLogFileReader reader = CreateReader(fileSink);
        fileSink.Information(Constants.DebugLogTestMessage);

        await reader.ClearAsync();

        string content = await File.ReadAllTextAsync(_testLogPath, TestContext.Current.CancellationToken);
        Assert.Empty(content);
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
    public async Task LoadAsync_WhenLogFileDeletedDuringRead_ShouldAllowDeletion()
    {
        using FileLogSink fileSink = CreateFileSink();
        DebugLogFileReader reader = CreateReader(fileSink);
        fileSink.Information(Constants.DebugLogLine1);
        fileSink.Information(Constants.DebugLogLine2);
        fileSink.Information(Constants.DebugLogLine3);

        await using IAsyncEnumerator<string> enumerator = reader.LoadAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Contains(Constants.DebugLogLine1, enumerator.Current);

        Exception? deleteException = Record.Exception(() => File.Delete(_testLogPath));

        Assert.Null(deleteException);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Contains(Constants.DebugLogLine2, enumerator.Current);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Contains(Constants.DebugLogLine3, enumerator.Current);

        Assert.False(await enumerator.MoveNextAsync());
    }

    [Fact]
    public async Task LoadAsync_WhenLogFileDoesNotExist_ShouldReturnNoLines()
    {
        if (File.Exists(_testLogPath))
        {
            File.Delete(_testLogPath);
        }

        using FileLogSink fileSink = CreateFileSink();
        DebugLogFileReader reader = CreateReader(fileSink);

        List<string> lines = await reader.LoadAsync(TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Empty(lines);
    }

    [Fact]
    public async Task LoadAsync_WhenLogFileHasContent_ShouldReturnAllLines()
    {
        using FileLogSink fileSink = CreateFileSink();
        DebugLogFileReader reader = CreateReader(fileSink);
        fileSink.Information(Constants.DebugLogLine1);
        fileSink.Warning(Constants.DebugLogLine2);
        fileSink.Error(Constants.DebugLogLine3);

        List<string> lines = await reader.LoadAsync(TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, lines.Count);
        Assert.Contains(Constants.DebugLogLine1, lines[0]);
        Assert.Contains(Constants.DebugLogLine2, lines[1]);
        Assert.Contains(Constants.DebugLogLine3, lines[2]);
    }

    [Fact]
    public async Task LoadAsync_WhenLogFileIsEmpty_ShouldReturnNoLines()
    {
        await File.WriteAllTextAsync(_testLogPath, string.Empty, TestContext.Current.CancellationToken);

        using FileLogSink fileSink = CreateFileSink();
        DebugLogFileReader reader = CreateReader(fileSink);

        List<string> lines = await reader.LoadAsync(TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Empty(lines);
    }

    private static LogRoutingPolicy CreateRoutingPolicy() =>
        new(LoggingOptions.CreateShippedDefaults(), LogLevel.Information);

    private FileLogSink CreateFileSink() =>
        new(_testLogPath, CreateRoutingPolicy(), DebugLogFormatter.Format);

    private DebugLogFileReader CreateReader(FileLogSink fileSink) =>
        new(new FileLocationOptions(_testDirectory), fileSink);
}

static file class DebugLogFileReaderTestLogging
{
    public static void Error(this FileLogSink sink, string message) => sink.Emit(new LogRecord(DateTime.UtcNow, LogLevel.Error, message));

    public static void Information(this FileLogSink sink, string message) => sink.Emit(new LogRecord(DateTime.UtcNow, LogLevel.Information, message));

    public static void Warning(this FileLogSink sink, string message) => sink.Emit(new LogRecord(DateTime.UtcNow, LogLevel.Warning, message));
}
