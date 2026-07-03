// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Sinks;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Logging.Tests.Sinks;

public sealed class UiStreamingSinkTests
{
    [Fact]
    public void Emit_AtOrAboveMinimum_ReportsTheRecord()
    {
        var captured = new List<LogRecord>();
        var sink = new UiStreamingSink(new CapturingProgress(captured), LogLevel.Information);
        var record = new LogRecord(DateTime.UtcNow, LogLevel.Warning, "warn");

        sink.Emit(record);

        Assert.Same(record, Assert.Single(captured));
    }

    [Fact]
    public void Emit_BelowMinimum_ReportsNothing()
    {
        var captured = new List<LogRecord>();
        var sink = new UiStreamingSink(new CapturingProgress(captured), LogLevel.Information);

        sink.Emit(new LogRecord(DateTime.UtcNow, LogLevel.Debug, "debug"));

        Assert.Empty(captured);
    }

    [Fact]
    public void MinimumLevelFor_ReturnsTheConfiguredLevel_RegardlessOfCategory()
    {
        var sink = new UiStreamingSink(new CapturingProgress([]), LogLevel.Trace);

        Assert.Equal(LogLevel.Trace, sink.MinimumLevelFor("DatabaseTools.Create"));
        Assert.Equal(LogLevel.Trace, sink.MinimumLevelFor("App"));
    }

    [Fact]
    public void Constructor_NullProgress_Throws() =>
        Assert.Throws<ArgumentNullException>(static () => new UiStreamingSink(null!, LogLevel.Information));

    [Fact]
    public void Emit_NullRecord_Throws()
    {
        var sink = new UiStreamingSink(new CapturingProgress([]), LogLevel.Information);

        Assert.Throws<ArgumentNullException>(() => sink.Emit(null!));
    }

    private sealed class CapturingProgress(List<LogRecord> captured) : IProgress<LogRecord>
    {
        public void Report(LogRecord value) => captured.Add(value);
    }
}
