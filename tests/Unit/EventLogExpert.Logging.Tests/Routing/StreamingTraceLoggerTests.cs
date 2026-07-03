// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Routing;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace EventLogExpert.Logging.Tests.Routing;

public sealed class StreamingTraceLoggerTests
{
    [Fact]
    public async Task ConcurrentEmit_FromManyThreads_NoExceptions_NoLostEntries()
    {
        // IProgress<T> via Progress<T> dispatches via SynchronizationContext if present, else
        // thread-pool. With a synchronous sink, dispatch is inline so all 10,000 entries are observed.
        var captured = new ConcurrentQueue<LogRecord>();
        var sink = new SynchronousProgress<LogRecord>(captured.Enqueue);
        ITraceLogger logger = new StreamingTraceLogger(sink);

        const int ThreadCount = 100;
        const int PerThread = 100;
        var tasks = new Task[ThreadCount];

        for (var t = 0; t < ThreadCount; t++)
        {
            var threadId = t;
            tasks[t] = Task.Run(() =>
            {
                for (var i = 0; i < PerThread; i++)
                {
                    logger.Information($"t{threadId} i{i}");
                }
            }, TestContext.Current.CancellationToken);
        }

        await Task.WhenAll(tasks);

        Assert.Equal(ThreadCount * PerThread, captured.Count);
    }

    [Fact]
    public void Constructor_NullLogSink_Throws() =>
        Assert.Throws<ArgumentNullException>(static () => new StreamingTraceLogger(null!));

    [Fact]
    public void Emit_AtDefaultMinimumLevel_DropsTraceAndDebug_KeepsInformationAndAbove()
    {
        var captured = new List<LogRecord>();
        var sink = new SynchronousProgress<LogRecord>(captured.Add);
        ITraceLogger logger = new StreamingTraceLogger(sink);

        logger.Trace($"trace");
        logger.Debug($"debug");
        logger.Information($"info");
        logger.Warning($"warning");
        logger.Error($"error");
        logger.Critical($"critical");

        Assert.Equal(4, captured.Count);
        Assert.Equal(LogLevel.Information, captured[0].Level);
        Assert.Equal(LogLevel.Warning, captured[1].Level);
        Assert.Equal(LogLevel.Error, captured[2].Level);
        Assert.Equal(LogLevel.Critical, captured[3].Level);
    }

    [Fact]
    public void Emit_PreservesInsertionOrder_OnSingleThread()
    {
        var captured = new List<LogRecord>();
        var sink = new SynchronousProgress<LogRecord>(captured.Add);
        ITraceLogger logger = new StreamingTraceLogger(sink);

        for (var i = 0; i < 100; i++)
        {
            logger.Information($"entry {i}");
        }

        Assert.Equal(100, captured.Count);
        for (var i = 0; i < 100; i++)
        {
            Assert.Contains($"entry {i}", captured[i].Message);
        }
    }

    [Fact]
    public void Emit_WithoutForCategory_LeavesCategoryEmpty_SoTheBroadcastLogProgressCanStampTheOperationCategory()
    {
        var captured = new List<LogRecord>();
        var sink = new SynchronousProgress<LogRecord>(captured.Add);
        ITraceLogger logger = new StreamingTraceLogger(sink, LogLevel.Trace);

        logger.Information($"uncategorized");

        Assert.Equal(string.Empty, Assert.Single(captured).Category);
    }

    [Fact]
    public void ForCategory_NullOrEmptyCategory_Throws()
    {
        ITraceLogger logger = new StreamingTraceLogger(new SynchronousProgress<LogRecord>(_ => { }));

        Assert.Throws<ArgumentNullException>(() => logger.ForCategory(null!));
        Assert.Throws<ArgumentException>(() => logger.ForCategory(string.Empty));
    }

    [Fact]
    public void ForCategory_StampsTheGivenCategory_OnEmittedRecords()
    {
        var captured = new List<LogRecord>();
        var sink = new SynchronousProgress<LogRecord>(captured.Add);
        ITraceLogger logger = new StreamingTraceLogger(sink, LogLevel.Trace);

        logger.ForCategory(LogCategories.OfflineWim).Information($"wim step");

        Assert.Equal(LogCategories.OfflineWim, Assert.Single(captured).Category);
    }

    [Fact]
    public void MinimumLevel_DefaultsToInformation_SoTraceAndDebugAreFiltered()
    {
        // Default Information level keeps verbose provider/reader chatter out of the UI log.
        var captured = new List<LogRecord>();
        ITraceLogger logger = new StreamingTraceLogger(new Progress<LogRecord>(captured.Add));

        Assert.Equal(LogLevel.Information, logger.MinimumLevel);
    }

    [Fact]
    public void MinimumLevel_HonorsConstructorArgument_WhenVerboseRequested()
    {
        var captured = new List<LogRecord>();
        ITraceLogger logger = new StreamingTraceLogger(new Progress<LogRecord>(captured.Add), LogLevel.Trace);

        Assert.Equal(LogLevel.Trace, logger.MinimumLevel);
    }

    [Fact]
    public void SeverityMapping_EachHandlerRoutesToCorrectLogLevel()
    {
        var captured = new List<LogRecord>();
        // Synchronous sink so we can assert without waiting for a synchronization-context post.
        var sink = new SynchronousProgress<LogRecord>(captured.Add);
        ITraceLogger logger = new StreamingTraceLogger(sink, LogLevel.Trace);

        logger.Trace($"trace");
        logger.Debug($"debug");
        logger.Information($"info");
        logger.Warning($"warning");
        logger.Error($"error");
        logger.Critical($"critical");

        Assert.Equal(6, captured.Count);
        Assert.Equal(LogLevel.Trace, captured[0].Level);
        Assert.Equal(LogLevel.Debug, captured[1].Level);
        Assert.Equal(LogLevel.Information, captured[2].Level);
        Assert.Equal(LogLevel.Warning, captured[3].Level);
        Assert.Equal(LogLevel.Error, captured[4].Level);
        Assert.Equal(LogLevel.Critical, captured[5].Level);
    }

    [Fact]
    public void Timestamp_IsCapturedAtCallTime()
    {
        var captured = new List<LogRecord>();
        var sink = new SynchronousProgress<LogRecord>(captured.Add);
        ITraceLogger logger = new StreamingTraceLogger(sink);

        var before = DateTime.UtcNow;
        logger.Information($"now");
        var after = DateTime.UtcNow;

        Assert.Single(captured);
        Assert.InRange(captured[0].TimestampUtc, before, after);
    }

    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
