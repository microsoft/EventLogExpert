// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Routing;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Logging.Tests.Routing;

public sealed class DispatchingTraceLoggerTests
{
    [Fact]
    public void Dispatch_BuildsOneRecord_AndHandsTheSameInstanceToEverySink()
    {
        var first = new RecordingSink(_ => LogLevel.Trace);
        var second = new RecordingSink(_ => LogLevel.Trace);
        ITraceLogger logger = new DispatchingTraceLogger([first, second], "App", ProcessOrigin.InProcess);

        logger.Information($"hello");

        var firstRecord = Assert.Single(first.Received);
        var secondRecord = Assert.Single(second.Received);

        Assert.Same(firstRecord, secondRecord);
        Assert.Equal("hello", firstRecord.Message);
        Assert.Equal(LogLevel.Information, firstRecord.Level);
    }

    [Fact]
    public void Dispatch_DoesNotEvaluateInterpolation_WhenBelowAggregateMinimum()
    {
        var warningsOnly = new RecordingSink(_ => LogLevel.Warning);
        ITraceLogger logger = new DispatchingTraceLogger([warningsOnly], "App", ProcessOrigin.InProcess);
        var evaluations = 0;

        logger.Information($"{Probe(ref evaluations)}");

        Assert.Equal(0, evaluations);
        Assert.Empty(warningsOnly.Received);
    }

    [Fact]
    public void Dispatch_EvaluatesInterpolation_WhenAtOrAboveAggregateMinimum()
    {
        var warningsOnly = new RecordingSink(_ => LogLevel.Warning);
        ITraceLogger logger = new DispatchingTraceLogger([warningsOnly], "App", ProcessOrigin.InProcess);
        var evaluations = 0;

        logger.Warning($"{Probe(ref evaluations)}");

        Assert.Equal(1, evaluations);
        var record = Assert.Single(warningsOnly.Received);
        Assert.Equal("probe", record.Message);
    }

    [Fact]
    public void Dispatch_StampsCategoryAndProcessOrigin_OnEveryRecord()
    {
        var sink = new RecordingSink(_ => LogLevel.Trace);
        ITraceLogger logger = new DispatchingTraceLogger([sink], "Offline.Wim", ProcessOrigin.ElevatedHelper);

        logger.Error($"boom");

        var record = Assert.Single(sink.Received);
        Assert.Equal("Offline.Wim", record.Category);
        Assert.Equal(ProcessOrigin.ElevatedHelper, record.ProcessOrigin);
    }

    [Fact]
    public void Emit_AppliesEachSinksOwnLevel_WhenSinksDisagree()
    {
        var verbose = new RecordingSink(_ => LogLevel.Trace);
        var warningsOnly = new RecordingSink(_ => LogLevel.Warning);
        ITraceLogger logger = new DispatchingTraceLogger([verbose, warningsOnly], "App", ProcessOrigin.InProcess);

        logger.Information($"info");
        logger.Warning($"warn");

        LogLevel[] verboseWrote = [.. verbose.Written.Select(record => record.Level)];
        LogLevel[] warningsOnlyWrote = [.. warningsOnly.Written.Select(record => record.Level)];

        Assert.Equal([LogLevel.Information, LogLevel.Warning], verboseWrote);
        Assert.Equal([LogLevel.Warning], warningsOnlyWrote);
    }

    [Fact]
    public void ForCategory_RecomputesMinimumLevel_FromTheNewCategory()
    {
        // The sink throttles App at Warning but Offline.Wim at Trace, so re-categorizing must re-key the level.
        var sink = new RecordingSink(category => category == LogCategories.OfflineWim ? LogLevel.Trace : LogLevel.Warning);
        ITraceLogger appLogger = new DispatchingTraceLogger([sink], "App", ProcessOrigin.InProcess);

        Assert.Equal(LogLevel.Warning, appLogger.MinimumLevel);
        Assert.Equal(LogLevel.Trace, appLogger.ForCategory(LogCategories.OfflineWim).MinimumLevel);
    }

    [Fact]
    public void ForCategory_ReturnsALoggerThatStampsTheNewCategory()
    {
        var sink = new RecordingSink(_ => LogLevel.Trace);
        ITraceLogger logger = new DispatchingTraceLogger([sink], "App", ProcessOrigin.InProcess);

        logger.ForCategory(LogCategories.OfflineHive).Warning($"hive parse");

        Assert.Equal(LogCategories.OfflineHive, Assert.Single(sink.Received).Category);
    }

    [Fact]
    public void MinimumLevel_IsTheLowestAcrossSinks()
    {
        var warningsOnly = new RecordingSink(_ => LogLevel.Warning);
        var verbose = new RecordingSink(_ => LogLevel.Debug);
        ITraceLogger logger = new DispatchingTraceLogger([warningsOnly, verbose], "App", ProcessOrigin.InProcess);

        Assert.Equal(LogLevel.Debug, logger.MinimumLevel);
    }

    [Fact]
    public void MinimumLevel_WithNoSinks_IsNone_SoNothingIsEverEnabled()
    {
        ITraceLogger logger = new DispatchingTraceLogger([], "App", ProcessOrigin.InProcess);

        Assert.Equal(LogLevel.None, logger.MinimumLevel);
    }

    [Fact]
    public void SeverityMapping_EachMethodRoutesToItsLogLevel()
    {
        var sink = new RecordingSink(_ => LogLevel.Trace);
        ITraceLogger logger = new DispatchingTraceLogger([sink], "App", ProcessOrigin.InProcess);

        logger.Trace($"t");
        logger.Debug($"d");
        logger.Information($"i");
        logger.Warning($"w");
        logger.Error($"e");
        logger.Critical($"c");

        LogLevel[] routed = [.. sink.Written.Select(record => record.Level)];

        Assert.Equal(
            [LogLevel.Trace, LogLevel.Debug, LogLevel.Information, LogLevel.Warning, LogLevel.Error, LogLevel.Critical],
            routed);
    }

    private static string Probe(ref int evaluations)
    {
        evaluations++;

        return "probe";
    }
}
