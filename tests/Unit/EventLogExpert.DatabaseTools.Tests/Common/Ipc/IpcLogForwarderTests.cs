// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Ipc;
using EventLogExpert.ElevationHelper.Ipc;
using EventLogExpert.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace EventLogExpert.DatabaseTools.Tests.Common.Ipc;

public sealed class IpcLogForwarderTests
{
    [Fact]
    public async Task Report_ForwardsTheRecordAsALogMessage_PreservingLevelMessageCategoryAndElevatedOrigin()
    {
        using var stream = new MemoryStream();

        await using (var writer = new IpcMessageWriter(stream))
        {
            IProgress<LogRecord> forwarder = new IpcLogForwarder(writer);
            forwarder.Report(new LogRecord(
                new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc),
                LogLevel.Warning,
                "resolve failed",
                LogCategories.Resolution));
        }

        stream.Position = 0;
        using var reader = new StreamReader(stream);
        string line = (await reader.ReadToEndAsync(TestContext.Current.CancellationToken)).Trim();
        var message = JsonSerializer.Deserialize<DatabaseToolsIpcMessage>(line, DatabaseToolsIpcSerializer.Options);

        var log = Assert.IsType<LogMessage>(message);
        Assert.Equal(LogLevel.Warning, log.Level);
        Assert.Equal("resolve failed", log.Message);
        Assert.Equal(LogCategories.Resolution, log.Category);
        Assert.Equal(ProcessOrigin.ElevatedHelper, log.ProcessOrigin);
    }

    [Fact]
    public void Report_WhenTheUnderlyingStreamIsClosed_SwallowsTheExceptionSoLoggingNeverBreaksTheOperation()
    {
        var stream = new MemoryStream();
        var writer = new IpcMessageWriter(stream);
        stream.Dispose();
        IProgress<LogRecord> forwarder = new IpcLogForwarder(writer);

        Exception? exception = Record.Exception(() => forwarder.Report(
            new LogRecord(DateTime.UtcNow, LogLevel.Information, "after close", LogCategories.EventLog)));

        Assert.Null(exception);
    }
}
