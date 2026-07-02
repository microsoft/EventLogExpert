// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Configuration;
using EventLogExpert.Logging.Routing;
using EventLogExpert.Logging.Sinks;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.DebugLog;
using EventLogExpert.Runtime.Settings;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Runtime.IntegrationTests.DebugLog;

public sealed class DebugLogHostTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly string _testLogPath;

    public DebugLogHostTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"DebugLogHostTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
        _testLogPath = Path.Combine(_testDirectory, "debug.log");
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
    public void Dispose_WhenSettingsChangeRaisedAfterDispose_ShouldDetachHandler()
    {
        TestSettingsService settings = new() { LogLevel = LogLevel.Information };
        LogRoutingPolicy routingPolicy = CreateRoutingPolicy(settings.LogLevel);
        using FileLogSink fileSink = new(_testLogPath, routingPolicy, DebugLogFormatter.Format);
        using DebugLogHost host = new(fileSink, routingPolicy, settings);

        host.Dispose();
        settings.LogLevel = LogLevel.Warning;
        settings.RaiseLogLevelChanged();

        Assert.Equal(LogLevel.Information, routingPolicy.FileMinimumFor(LogSourceFactory.DefaultCategory));
    }

    [Fact]
    public void EmitUnfiltered_WhenBaselineIsNone_ShouldWriteCriticalLine()
    {
        TestSettingsService settings = new() { LogLevel = LogLevel.None };
        LogRoutingPolicy routingPolicy = CreateRoutingPolicy(settings.LogLevel);
        using FileLogSink fileSink = new(_testLogPath, routingPolicy, DebugLogFormatter.Format);
        using DebugLogHost host = new(fileSink, routingPolicy, settings);

        fileSink.EmitUnfiltered(new LogRecord(DateTime.UtcNow, LogLevel.Critical, "fatal message"));

        string content = ReadLogFile();
        Assert.Contains("fatal message", content);
        Assert.Contains("[Critical]", content);
    }

    [Fact]
    public void MinimumLevelFor_WhenLogLevelChangedAtRuntime_ShouldReflectNewLevel()
    {
        TestSettingsService settings = new() { LogLevel = LogLevel.Information };
        LogRoutingPolicy routingPolicy = CreateRoutingPolicy(settings.LogLevel);
        using FileLogSink fileSink = new(_testLogPath, routingPolicy, DebugLogFormatter.Format);
        using DebugLogHost host = new(fileSink, routingPolicy, settings);

        Assert.Equal(LogLevel.Information, routingPolicy.FileMinimumFor(LogSourceFactory.DefaultCategory));

        settings.LogLevel = LogLevel.Warning;
        settings.RaiseLogLevelChanged();

        Assert.Equal(LogLevel.Warning, routingPolicy.FileMinimumFor(LogSourceFactory.DefaultCategory));
    }

    [Fact]
    public void Trace_WhenLogLevelChangedAtRuntime_ShouldRespectNewLevel()
    {
        TestSettingsService settings = new() { LogLevel = LogLevel.Information };
        LogRoutingPolicy routingPolicy = CreateRoutingPolicy(settings.LogLevel);
        using FileLogSink fileSink = new(_testLogPath, routingPolicy, DebugLogFormatter.Format);
        using DebugLogHost host = new(fileSink, routingPolicy, settings);

        fileSink.Information("message before change");
        settings.LogLevel = LogLevel.Warning;
        settings.RaiseLogLevelChanged();
        fileSink.Information("filtered information after change");
        fileSink.Warning("warning message after change");

        string content = ReadLogFile();
        Assert.Contains("message before change", content);
        Assert.DoesNotContain("filtered information after change", content);
        Assert.Contains("warning message after change", content);
    }

    [Fact]
    public void TraceIfEnabled_WhenLogLevelChangedAtRuntime_ShouldRespectNewLevel()
    {
        TestSettingsService settings = new() { LogLevel = LogLevel.Debug };
        LogRoutingPolicy routingPolicy = CreateRoutingPolicy(settings.LogLevel);
        using FileLogSink fileSink = new(_testLogPath, routingPolicy, DebugLogFormatter.Format);
        using DebugLogHost host = new(fileSink, routingPolicy, settings);

        fileSink.Debug("debug message before change");
        string contentBefore = ReadLogFile();
        Assert.Contains("debug message before change", contentBefore);

        settings.LogLevel = LogLevel.Warning;
        settings.RaiseLogLevelChanged();
        fileSink.Debug("debug message after change");
        fileSink.Warning("warning message after change");

        string content = ReadLogFile();
        Assert.Contains("debug message before change", content);
        Assert.DoesNotContain("debug message after change", content);
        Assert.Contains("warning message after change", content);
    }

    private static LogRoutingPolicy CreateRoutingPolicy(LogLevel logLevel) =>
        new(LoggingOptions.CreateShippedDefaults(), logLevel);

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

    private sealed class TestSettingsService : ISettingsService
    {
        public EventCopyFormat CopyFormat { get; set; }

        public Action? CopyFormatChanged { get; set; }

        public bool HasEverEnabledPreRelease => false;

        public bool IsPreReleaseEnabled { get; set; }

        public LogLevel LogLevel { get; set; }

        public Action? LogLevelChanged { get; set; }

        public Theme Theme { get; set; }

        public Action? ThemeChanged { get; set; }

        public EventHandler<TimeZoneInfo>? TimeZoneChanged { get; set; }

        public string TimeZoneId { get; set; } = TimeZoneInfo.Local.Id;

        public TimeZoneInfo TimeZoneInfo => TimeZoneInfo.Local;

        public void RaiseLogLevelChanged() => LogLevelChanged?.Invoke();
    }
}

static file class DebugLogHostTestLogging
{
    public static void Debug(this FileLogSink sink, string message) => sink.Emit(new LogRecord(DateTime.UtcNow, LogLevel.Debug, message));

    public static void Information(this FileLogSink sink, string message) => sink.Emit(new LogRecord(DateTime.UtcNow, LogLevel.Information, message));

    public static void Warning(this FileLogSink sink, string message) => sink.Emit(new LogRecord(DateTime.UtcNow, LogLevel.Warning, message));
}
