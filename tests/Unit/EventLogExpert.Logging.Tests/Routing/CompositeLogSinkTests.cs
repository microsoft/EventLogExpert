// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Routing;
using EventLogExpert.Logging.Sinks;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Logging.Tests.Routing;

public sealed class CompositeLogSinkTests
{
    [Fact]
    public void Report_EachSinkAppliesItsOwnLevel_ReproducingTheUiAllFileWarningSplit()
    {
        var ui = new RecordingSink(_ => LogLevel.Information);
        var file = new RecordingSink(_ => LogLevel.Warning);
        var composite = new CompositeLogSink([ui, file], "DatabaseTools.Create");

        composite.Report(new LogRecord(DateTime.UtcNow, LogLevel.Information, "progress"));
        composite.Report(new LogRecord(DateTime.UtcNow, LogLevel.Warning, "problem"));

        Assert.Equal([LogLevel.Information, LogLevel.Warning], ui.Written.Select(record => record.Level).ToArray());
        Assert.Equal([LogLevel.Warning], file.Written.Select(record => record.Level).ToArray());
    }

    [Fact]
    public void Report_FansEachRecordToEverySink()
    {
        var first = new RecordingSink(_ => LogLevel.Trace);
        var second = new RecordingSink(_ => LogLevel.Trace);
        var composite = new CompositeLogSink([first, second], "DatabaseTools.Create");

        composite.Report(new LogRecord(DateTime.UtcNow, LogLevel.Warning, "warn"));

        Assert.Single(first.Received);
        Assert.Single(second.Received);
    }

    [Fact]
    public void Report_PreservesExistingOrigin_WhenAlreadyCategorized()
    {
        var sink = new RecordingSink(_ => LogLevel.Trace);
        var composite = new CompositeLogSink([sink], "DatabaseTools.Create");

        composite.Report(new LogRecord(DateTime.UtcNow, LogLevel.Information, "hi", "Offline.Wim"));

        Assert.Equal("Offline.Wim", Assert.Single(sink.Received).Category);
    }

    [Fact]
    public void Report_StampsOperationCategory_WhenRecordIsUncategorized()
    {
        var sink = new RecordingSink(_ => LogLevel.Trace);
        var composite = new CompositeLogSink([sink], "DatabaseTools.Create");

        composite.Report(new LogRecord(DateTime.UtcNow, LogLevel.Information, "hi"));

        Assert.Equal("DatabaseTools.Create", Assert.Single(sink.Received).Category);
    }

    [Fact]
    public void Report_WhenASinkThrows_IsolatesTheFault_AndStillReachesTheOtherSinks()
    {
        var healthy = new RecordingSink(_ => LogLevel.Trace);
        var composite = new CompositeLogSink([new ThrowingSink(), healthy], "DatabaseTools.Create");

        Exception? exception = Record.Exception(() =>
            composite.Report(new LogRecord(DateTime.UtcNow, LogLevel.Warning, "warn")));

        Assert.Null(exception);
        Assert.Single(healthy.Received);
    }

    private sealed class ThrowingSink : ILogSink
    {
        public void Emit(LogRecord record) => throw new IOException("sink down");

        public LogLevel MinimumLevelFor(string category) => LogLevel.Trace;
    }
}
