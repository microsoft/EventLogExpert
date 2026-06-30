// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using EventLogExpert.Logging.Sinks;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Logging.Tests.Sinks;

public sealed class FileTeeLogSinkTests
{
    [Fact]
    public void Report_ErrorAndCritical_TeeToFileAtMatchingLevels()
    {
        var fileLogger = new RecordingTraceLogger();
        var captured = new List<LogRecord>();
        var sink = new FileTeeLogSink(new RecordingProgress(captured), fileLogger);

        sink.Report(new LogRecord(DateTime.UtcNow, LogLevel.Error, "error line"));
        sink.Report(new LogRecord(DateTime.UtcNow, LogLevel.Critical, "critical line"));

        Assert.Equal(2, fileLogger.Entries.Count);
        Assert.Equal(LogLevel.Error, fileLogger.Entries[0].Level);
        Assert.Equal(LogLevel.Critical, fileLogger.Entries[1].Level);
        Assert.Equal(2, captured.Count);
    }

    [Fact]
    public void Report_InformationRecord_ForwardsToInnerOnly_WithoutTeeingToFile()
    {
        var fileLogger = new RecordingTraceLogger();
        var captured = new List<LogRecord>();
        var sink = new FileTeeLogSink(new RecordingProgress(captured), fileLogger);

        sink.Report(new LogRecord(DateTime.UtcNow, LogLevel.Information, "progress line"));
        sink.Report(new LogRecord(DateTime.UtcNow, LogLevel.Debug, "debug line"));
        sink.Report(new LogRecord(DateTime.UtcNow, LogLevel.Trace, "trace line"));

        Assert.Empty(fileLogger.Entries);
        Assert.Equal(3, captured.Count);
    }

    [Fact]
    public void Report_WarningRecord_TeesToFileThenForwardsToInner()
    {
        var sequence = new List<string>();
        var fileLogger = new RecordingTraceLogger(sequence);
        var captured = new List<LogRecord>();
        var inner = new RecordingProgress(captured, sequence);

        var sink = new FileTeeLogSink(inner, fileLogger);
        var record = new LogRecord(DateTime.UtcNow, LogLevel.Warning, "no providers were discovered");

        sink.Report(record);

        var fileEntry = Assert.Single(fileLogger.Entries);
        Assert.Equal(LogLevel.Warning, fileEntry.Level);
        Assert.Equal("no providers were discovered", fileEntry.Message);
        Assert.Equal(record, Assert.Single(captured));
        // The persistent file write must happen before the record is handed to the inner (UI) sink.
        Assert.Equal(["file", "inner"], sequence);
    }

    [Fact]
    public void Report_WhenFileLoggerThrows_StillForwardsToInner_AndSwallowsTheFailure()
    {
        var captured = new List<LogRecord>();
        var sink = new FileTeeLogSink(new RecordingProgress(captured), new ThrowingTraceLogger());
        var record = new LogRecord(DateTime.UtcNow, LogLevel.Error, "error while the file logger is broken");

        // A file-logging failure must never break the operation or the UI, and the record must still reach the inner
        // sink so it remains visible in the UI log.
        var exception = Record.Exception(() => sink.Report(record));

        Assert.Null(exception);
        Assert.Equal(record, Assert.Single(captured));
    }

    private sealed class RecordingProgress(List<LogRecord> captured, List<string>? sequence = null) : IProgress<LogRecord>
    {
        public void Report(LogRecord value)
        {
            captured.Add(value);
            sequence?.Add("inner");
        }
    }

    private sealed class RecordingTraceLogger(List<string>? sequence = null) : ITraceLogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public LogLevel MinimumLevel => LogLevel.Trace;

        public void Critical(CriticalLogHandler handler) => Record(LogLevel.Critical, handler.ToStringAndClear());

        public void Debug(DebugLogHandler handler) => Record(LogLevel.Debug, handler.ToStringAndClear());

        public void Error(ErrorLogHandler handler) => Record(LogLevel.Error, handler.ToStringAndClear());

        public void Information(InformationLogHandler handler) => Record(LogLevel.Information, handler.ToStringAndClear());

        public void Trace(TraceLogHandler handler) => Record(LogLevel.Trace, handler.ToStringAndClear());

        public void Warning(WarningLogHandler handler) => Record(LogLevel.Warning, handler.ToStringAndClear());

        private void Record(LogLevel level, string message)
        {
            Entries.Add((level, message));
            sequence?.Add("file");
        }
    }

    private sealed class ThrowingTraceLogger : ITraceLogger
    {
        public LogLevel MinimumLevel => LogLevel.Trace;

        public void Critical(CriticalLogHandler handler) => throw new IOException("file logger is broken");

        public void Debug(DebugLogHandler handler) => throw new IOException("file logger is broken");

        public void Error(ErrorLogHandler handler) => throw new IOException("file logger is broken");

        public void Information(InformationLogHandler handler) => throw new IOException("file logger is broken");

        public void Trace(TraceLogHandler handler) => throw new IOException("file logger is broken");

        public void Warning(WarningLogHandler handler) => throw new IOException("file logger is broken");
    }
}
