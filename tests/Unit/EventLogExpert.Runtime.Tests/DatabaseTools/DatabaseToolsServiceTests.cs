// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Contracts;
using EventLogExpert.DatabaseTools.Operations;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.DatabaseTools;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace EventLogExpert.Runtime.Tests.DatabaseTools;

public sealed class DatabaseToolsServiceTests
{
    [Fact]
    public async Task AllLogCallsFromOperation_ReachSink_InOrder()
    {
        var fake = new RecordingOperation(logger =>
        {
            logger.Information($"one");
            logger.Information($"two");
            logger.Warning($"three");
            return Task.FromResult(DatabaseToolsOutcome.Succeeded);
        });
        var service = new TestableDatabaseToolsService(fake);
        var logSink = new ListProgress<DatabaseToolsLogEntry>();

        await service.ShowAsync(
            new ShowProvidersRequest(null, null), logSink, progress: null, CancellationToken.None);

        Assert.Equal(3, logSink.Entries.Count);
        Assert.Contains("one", logSink.Entries[0].Message);
        Assert.Contains("two", logSink.Entries[1].Message);
        Assert.Contains("three", logSink.Entries[2].Message);
        Assert.Equal(LogLevel.Warning, logSink.Entries[2].Level);
    }

    [Fact]
    public async Task DurationIsMeasured_AndGreaterThanZero()
    {
        // The Operation does a small sleep so the elapsed timer captures a non-zero interval.
        var fake = new RecordingOperation(async _ =>
        {
            await Task.Delay(50);
            return DatabaseToolsOutcome.Succeeded;
        });
        var service = new TestableDatabaseToolsService(fake);
        var logSink = new ListProgress<DatabaseToolsLogEntry>();

        var result = await service.ShowAsync(
            new ShowProvidersRequest(null, null), logSink, progress: null, CancellationToken.None);

        Assert.True(result.Duration > TimeSpan.Zero, $"Duration was {result.Duration}.");
    }

    [Fact]
    public async Task OperationSucceeds_ResultOutcomeMatches_FailureSummaryNull()
    {
        var fake = new RecordingOperation(DatabaseToolsOutcome.Succeeded);
        var service = new TestableDatabaseToolsService(fake);
        var logSink = new ListProgress<DatabaseToolsLogEntry>();

        var result = await service.ShowAsync(
            new ShowProvidersRequest(null, null), logSink, progress: null, CancellationToken.None);

        Assert.Equal(DatabaseToolsOutcome.Succeeded, result.Outcome);
        Assert.Null(result.FailureSummary);
    }

    [Fact]
    public async Task OperationThrowsException_ResultIsFailed_FailureSummarySet_ErrorLogged()
    {
        var fake = new RecordingOperation(_ => throw new InvalidOperationException("boom"));
        var service = new TestableDatabaseToolsService(fake);
        var logSink = new ListProgress<DatabaseToolsLogEntry>();

        var result = await service.ShowAsync(
            new ShowProvidersRequest(null, null), logSink, progress: null, CancellationToken.None);

        Assert.Equal(DatabaseToolsOutcome.Failed, result.Outcome);
        Assert.Equal("boom", result.FailureSummary);
        Assert.Contains(logSink.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task OperationThrowsOperationCanceledException_ResultIsCancelled()
    {
        var fake = new RecordingOperation(_ => throw new OperationCanceledException());
        var service = new TestableDatabaseToolsService(fake);
        var logSink = new ListProgress<DatabaseToolsLogEntry>();

        var result = await service.ShowAsync(
            new ShowProvidersRequest(null, null), logSink, progress: null, CancellationToken.None);

        Assert.Equal(DatabaseToolsOutcome.Cancelled, result.Outcome);
        Assert.Null(result.FailureSummary);
    }

    [Fact]
    public async Task PreCancelledToken_ReturnsCancelled_BeforeOperationInvoked()
    {
        var fake = new RecordingOperation(DatabaseToolsOutcome.Succeeded);
        var service = new TestableDatabaseToolsService(fake);
        var logSink = new ListProgress<DatabaseToolsLogEntry>();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await service.ShowAsync(
            new ShowProvidersRequest(null, null), logSink, progress: null, cts.Token);

        Assert.Equal(DatabaseToolsOutcome.Cancelled, result.Outcome);
        Assert.False(fake.WasInvoked, "Operation must not run when token is already cancelled.");
    }

    private sealed class ListProgress<T> : IProgress<T>
    {
        public List<T> Entries { get; } = [];

        public void Report(T value) => Entries.Add(value);
    }

    private sealed class RecordingOperation : IDatabaseToolsOperation
    {
        private readonly Func<ITraceLogger, Task<DatabaseToolsOutcome>> _body;

        public RecordingOperation(DatabaseToolsOutcome outcome)
            : this(_ => Task.FromResult(outcome)) { }

        public RecordingOperation(Func<ITraceLogger, Task<DatabaseToolsOutcome>> body) => _body = body;

        public RecordingOperation(Action<ITraceLogger> body)
            : this(logger => { body(logger); return Task.FromResult(DatabaseToolsOutcome.Succeeded); }) { }

        public bool WasInvoked { get; private set; }

        public Task<DatabaseToolsOutcome> ExecuteAsync(
            ITraceLogger logger,
            IProgress<DatabaseToolsProgress>? progress,
            CancellationToken cancellationToken)
        {
            WasInvoked = true;
            return _body(logger);
        }
    }

    // ---- Test doubles ----

    private sealed class TestableDatabaseToolsService(IDatabaseToolsOperation operation) : IDatabaseToolsService
    {
        public Task<DatabaseToolsResult> CreateAsync(CreateDatabaseRequest request, IProgress<DatabaseToolsLogEntry> logSink, IProgress<DatabaseToolsProgress>? progress, CancellationToken cancellationToken, bool verbose = false)
            => InvokeOperation(operation, logSink, progress, cancellationToken, verbose);

        public Task<DatabaseToolsResult> DiffAsync(DiffDatabaseRequest request, IProgress<DatabaseToolsLogEntry> logSink, IProgress<DatabaseToolsProgress>? progress, CancellationToken cancellationToken, bool verbose = false)
            => InvokeOperation(operation, logSink, progress, cancellationToken, verbose);

        public Task<DatabaseToolsResult> MergeAsync(MergeDatabaseRequest request, IProgress<DatabaseToolsLogEntry> logSink, IProgress<DatabaseToolsProgress>? progress, CancellationToken cancellationToken, bool verbose = false)
            => InvokeOperation(operation, logSink, progress, cancellationToken, verbose);

        public Task<DatabaseToolsResult> ShowAsync(
            ShowProvidersRequest request,
            IProgress<DatabaseToolsLogEntry> logSink,
            IProgress<DatabaseToolsProgress>? progress,
            CancellationToken cancellationToken,
            bool verbose = false)
            => InvokeOperation(operation, logSink, progress, cancellationToken, verbose);

        public Task<DatabaseToolsResult> UpgradeAsync(UpgradeDatabaseRequest request, IProgress<DatabaseToolsLogEntry> logSink, IProgress<DatabaseToolsProgress>? progress, CancellationToken cancellationToken, bool verbose = false)
            => InvokeOperation(operation, logSink, progress, cancellationToken, verbose);

        private static async Task<DatabaseToolsResult> InvokeOperation(
            IDatabaseToolsOperation op,
            IProgress<DatabaseToolsLogEntry> logSink,
            IProgress<DatabaseToolsProgress>? progress,
            CancellationToken cancellationToken,
            bool verbose)
        {
            ITraceLogger logger = new StreamingTraceLogger(logSink, verbose ? LogLevel.Trace : LogLevel.Information);
            var sw = Stopwatch.StartNew();
            DatabaseToolsOutcome outcome;
            string? failure = null;

            try
            {
                outcome = await Task.Run(() => op.ExecuteAsync(logger, progress, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                outcome = DatabaseToolsOutcome.Cancelled;
            }
            catch (Exception ex)
            {
                outcome = DatabaseToolsOutcome.Failed;
                failure = ex.Message;
                logger.Error($"{ex}");
            }

            sw.Stop();
            return new DatabaseToolsResult(outcome, failure, sw.Elapsed);
        }
    }
}
