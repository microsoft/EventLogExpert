// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Sinks;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Logging.Tests.Sinks;

public sealed class UIStreamingSinkTests
{
    [Fact]
    public void Constructor_NullProgress_Throws() =>
        Assert.Throws<ArgumentNullException>(static () => new UIStreamingSink(null!, LogLevel.Information));

    [Fact]
    public void Emit_AtOrAboveMinimum_ReportsTheRecord()
    {
        var captured = new List<LogRecord>();
        var sink = new UIStreamingSink(new CapturingProgress(captured), LogLevel.Information);
        var record = new LogRecord(DateTime.UtcNow, LogLevel.Warning, "warn");

        sink.Emit(record);

        Assert.Equal(record, Assert.Single(captured));
    }

    [Fact]
    public void Emit_BelowMinimum_ReportsNothing()
    {
        var captured = new List<LogRecord>();
        var sink = new UIStreamingSink(new CapturingProgress(captured), LogLevel.Information);

        sink.Emit(new LogRecord(DateTime.UtcNow, LogLevel.Debug, "debug"));

        Assert.Empty(captured);
    }

    [Fact]
    public void Emit_DiagnosticAudience_ReportsNothing()
    {
        var captured = new List<LogRecord>();
        var sink = new UIStreamingSink(new CapturingProgress(captured), LogLevel.Information);

        // Trace-channel diagnostics carry unlocalized English; they must never reach the user-facing operation log.
        sink.Emit(new LogRecord(DateTime.UtcNow, LogLevel.Error, "diagnostic", Audience: LogAudience.Diagnostic));

        Assert.Empty(captured);
    }

    [Fact]
    public void Emit_NullRecord_Throws()
    {
        var sink = new UIStreamingSink(new CapturingProgress([]), LogLevel.Information);

        Assert.Throws<ArgumentNullException>(() => sink.Emit(null!));
    }

    [Fact]
    public void Emit_UserAudience_ReportsTheRecord()
    {
        var captured = new List<LogRecord>();
        var sink = new UIStreamingSink(new CapturingProgress(captured), LogLevel.Information);
        var record = new LogRecord(DateTime.UtcNow, LogLevel.Information, "visible", Audience: LogAudience.User);

        sink.Emit(record);

        Assert.Equal(record, Assert.Single(captured));
    }

    [Fact]
    public void Emit_WithDebugDetail_StripsDebugDetailBeforeReporting()
    {
        var captured = new List<LogRecord>();
        var sink = new UIStreamingSink(new CapturingProgress(captured), LogLevel.Information);

        sink.Emit(new LogRecord(DateTime.UtcNow, LogLevel.Error, "visible", DebugDetail: "hidden stack"));

        Assert.Null(Assert.Single(captured).DebugDetail);
    }

    [Fact]
    public void MinimumLevelFor_ReturnsTheConfiguredLevel_RegardlessOfCategory()
    {
        var sink = new UIStreamingSink(new CapturingProgress([]), LogLevel.Trace);

        Assert.Equal(LogLevel.Trace, sink.MinimumLevelFor("DatabaseTools.Create"));
        Assert.Equal(LogLevel.Trace, sink.MinimumLevelFor("App"));
    }

    private sealed class CapturingProgress(List<LogRecord> captured) : IProgress<LogRecord>
    {
        public void Report(LogRecord value) => captured.Add(value);
    }
}
