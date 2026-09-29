// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Loggers;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Logging.Tests;

public sealed class StreamingOperationLogPluralTests
{
    [Fact]
    public void User_WithPluralCount_CarriesCountIntoLogRecord()
    {
        var captured = new List<LogRecord>();
        var log = new StreamingOperationLog(new CapturingProgress(captured));

        log.User(
            LogLevel.Information,
            new LocalizableText("DatabaseTools_Op_CreateSkippedProviders", ["file.db"], PluralCount: 3));

        var record = Assert.Single(captured);
        Assert.Equal("DatabaseTools_Op_CreateSkippedProviders", record.MessageKey);
        Assert.Equal(["file.db"], record.MessageArgs);
        Assert.Equal(3L, record.MessagePluralCount);
    }

    [Fact]
    public void User_WithoutPluralCount_LeavesMessagePluralCountNull()
    {
        var captured = new List<LogRecord>();
        var log = new StreamingOperationLog(new CapturingProgress(captured));

        log.User(LogLevel.Information, new LocalizableText("DatabaseTools_Op_Test", ["x"]));

        Assert.Null(Assert.Single(captured).MessagePluralCount);
    }

    private sealed class CapturingProgress(List<LogRecord> captured) : IProgress<LogRecord>
    {
        public void Report(LogRecord value) => captured.Add(value);
    }
}
