// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Contracts;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.DatabaseTools;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Runtime.Tests.DatabaseTools;

public sealed class StreamingTraceLoggerTests
{
    [Fact]
    public void MinimumLevel_IsTrace_SoEveryHandlerMaterializesItsMessage()
    {
        // Regression guard for the codebase rule that interpolated-string handlers short-circuit
        // (and ToStringAndClear returns "") whenever MinimumLevel is higher than the handler's level.
        var captured = new List<DatabaseToolsLogEntry>();
        ITraceLogger logger = new StreamingTraceLogger(new Progress<DatabaseToolsLogEntry>(captured.Add));

        Assert.Equal(LogLevel.Trace, logger.MinimumLevel);
    }

    [Fact]
    public void SeverityMapping_EachHandlerRoutesToCorrectLogLevel()
    {
        var captured = new List<DatabaseToolsLogEntry>();
        // Synchronous sink so we can assert without waiting for a synchronization-context post.
        var sink = new SynchronousProgress<DatabaseToolsLogEntry>(captured.Add);
        ITraceLogger logger = new StreamingTraceLogger(sink);

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
    public void Emit_PreservesInsertionOrder_OnSingleThread()
    {
        var captured = new List<DatabaseToolsLogEntry>();
        var sink = new SynchronousProgress<DatabaseToolsLogEntry>(captured.Add);
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
    public void Timestamp_IsCapturedAtCallTime()
    {
        var captured = new List<DatabaseToolsLogEntry>();
        var sink = new SynchronousProgress<DatabaseToolsLogEntry>(captured.Add);
        ITraceLogger logger = new StreamingTraceLogger(sink);

        var before = DateTime.UtcNow;
        logger.Information($"now");
        var after = DateTime.UtcNow;

        Assert.Single(captured);
        Assert.InRange(captured[0].TimestampUtc, before, after);
    }

    [Fact]
    public async Task ConcurrentEmit_FromManyThreads_NoExceptions_NoLostEntries()
    {
        // IProgress<T> via Progress<T> dispatches via SynchronizationContext if present, else
        // thread-pool. With a synchronous sink, dispatch is inline so all 10,000 entries are observed.
        var captured = new System.Collections.Concurrent.ConcurrentQueue<DatabaseToolsLogEntry>();
        var sink = new SynchronousProgress<DatabaseToolsLogEntry>(captured.Enqueue);
        ITraceLogger logger = new StreamingTraceLogger(sink);

        const int threadCount = 100;
        const int perThread = 100;
        var tasks = new Task[threadCount];

        for (var t = 0; t < threadCount; t++)
        {
            var threadId = t;
            tasks[t] = Task.Run(() =>
            {
                for (var i = 0; i < perThread; i++)
                {
                    logger.Information($"t{threadId} i{i}");
                }
            }, TestContext.Current.CancellationToken);
        }

        await Task.WhenAll(tasks);

        Assert.Equal(threadCount * perThread, captured.Count);
    }

    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
