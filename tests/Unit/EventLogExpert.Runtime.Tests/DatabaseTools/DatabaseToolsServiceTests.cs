// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.DatabaseTools.CreateDatabase;
using EventLogExpert.DatabaseTools.DiffDatabase;
using EventLogExpert.DatabaseTools.MergeDatabase;
using EventLogExpert.DatabaseTools.ShowProviders;
using EventLogExpert.DatabaseTools.UpgradeDatabase;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.DatabaseTools;
using Microsoft.Extensions.Logging;

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
        var (service, _) = CreateSut(showOperation: () => fake);
        var logProgress = new ListProgress<LogRecord>();

        await service.ShowAsync(
            new ShowProvidersRequest(null, null), logProgress, progress: null, CancellationToken.None);

        Assert.Equal(3, logProgress.Entries.Count);
        Assert.Contains("one", logProgress.Entries[0].Message);
        Assert.Contains("two", logProgress.Entries[1].Message);
        Assert.Contains("three", logProgress.Entries[2].Message);
        Assert.Equal(LogLevel.Warning, logProgress.Entries[2].Level);
    }

    [Fact]
    public async Task DurationIsMeasured_AndGreaterThanZero()
    {
        var fake = new RecordingOperation(async _ =>
        {
            await Task.Delay(50);
            return DatabaseToolsOutcome.Succeeded;
        });
        var (service, _) = CreateSut(showOperation: () => fake);
        var logProgress = new ListProgress<LogRecord>();

        var result = await service.ShowAsync(
            new ShowProvidersRequest(null, null), logProgress, progress: null, CancellationToken.None);

        Assert.True(result.Duration > TimeSpan.Zero, $"Duration was {result.Duration}.");
    }

    [Fact]
    public async Task OperationSucceeds_ResultOutcomeMatches_FailureSummaryNull()
    {
        var fake = new RecordingOperation(DatabaseToolsOutcome.Succeeded);
        var (service, _) = CreateSut(showOperation: () => fake);
        var logProgress = new ListProgress<LogRecord>();

        var result = await service.ShowAsync(
            new ShowProvidersRequest(null, null), logProgress, progress: null, CancellationToken.None);

        Assert.Equal(DatabaseToolsOutcome.Succeeded, result.Outcome);
        Assert.Null(result.FailureSummary);
    }

    [Fact]
    public async Task OperationThrowsException_ResultIsFailed_FailureSummarySet_ErrorLogged()
    {
        var fake = new RecordingOperation(_ => throw new InvalidOperationException("boom"));
        var (service, _) = CreateSut(showOperation: () => fake);
        var logProgress = new ListProgress<LogRecord>();

        var result = await service.ShowAsync(
            new ShowProvidersRequest(null, null), logProgress, progress: null, CancellationToken.None);

        Assert.Equal(DatabaseToolsOutcome.Failed, result.Outcome);
        Assert.Equal("boom", result.FailureSummary);
        Assert.Contains(logProgress.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task OperationThrowsOperationCanceledException_ResultIsCancelled()
    {
        var fake = new RecordingOperation(_ => throw new OperationCanceledException());
        var (service, _) = CreateSut(showOperation: () => fake);
        var logProgress = new ListProgress<LogRecord>();

        var result = await service.ShowAsync(
            new ShowProvidersRequest(null, null), logProgress, progress: null, CancellationToken.None);

        Assert.Equal(DatabaseToolsOutcome.Cancelled, result.Outcome);
        Assert.Null(result.FailureSummary);
    }

    [Fact]
    public async Task PreCancelledToken_ReturnsCancelled_BeforeOperationInvoked()
    {
        var fake = new RecordingOperation(DatabaseToolsOutcome.Succeeded);
        var (service, _) = CreateSut(showOperation: () => fake);
        var logProgress = new ListProgress<LogRecord>();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await service.ShowAsync(
            new ShowProvidersRequest(null, null), logProgress, progress: null, cts.Token);

        Assert.Equal(DatabaseToolsOutcome.Cancelled, result.Outcome);
        Assert.False(fake.WasInvoked, "Operation must not run when token is already cancelled.");
    }

    [Theory]
    [InlineData("Show")]
    [InlineData("Create")]
    [InlineData("Merge")]
    [InlineData("Diff")]
    [InlineData("Upgrade")]
    public async Task PublicMethod_DispatchesViaCorrectFactoryOverload(string method)
    {
        var fake = new RecordingOperation(DatabaseToolsOutcome.Succeeded);
        var (service, factory) = CreateSut(
            showOperation: () => fake,
            createOperation: () => fake,
            mergeOperation: () => fake,
            diffOperation: () => fake,
            upgradeOperation: () => fake);
        var logProgress = new ListProgress<LogRecord>();

        switch (method)
        {
            case "Show":
                await service.ShowAsync(new ShowProvidersRequest(null, null), logProgress, progress: null, CancellationToken.None);
                Assert.Equal(1, factory.ShowCallCount);
                Assert.Equal(0, factory.CreateCallCount + factory.MergeCallCount + factory.DiffCallCount + factory.UpgradeCallCount);
                break;
            case "Create":
                await service.CreateAsync(new CreateDatabaseRequest("target.db", null, null, null), logProgress, progress: null, CancellationToken.None);
                Assert.Equal(1, factory.CreateCallCount);
                Assert.Equal(0, factory.ShowCallCount + factory.MergeCallCount + factory.DiffCallCount + factory.UpgradeCallCount);
                break;
            case "Merge":
                await service.MergeAsync(new MergeDatabaseRequest("source.db", "target.db", false), logProgress, progress: null, CancellationToken.None);
                Assert.Equal(1, factory.MergeCallCount);
                Assert.Equal(0, factory.ShowCallCount + factory.CreateCallCount + factory.DiffCallCount + factory.UpgradeCallCount);
                break;
            case "Diff":
                await service.DiffAsync(new DiffDatabaseRequest("first.db", "second.db", "out.db"), logProgress, progress: null, CancellationToken.None);
                Assert.Equal(1, factory.DiffCallCount);
                Assert.Equal(0, factory.ShowCallCount + factory.CreateCallCount + factory.MergeCallCount + factory.UpgradeCallCount);
                break;
            case "Upgrade":
                await service.UpgradeAsync(new UpgradeDatabaseRequest("target.db"), logProgress, progress: null, CancellationToken.None);
                Assert.Equal(1, factory.UpgradeCallCount);
                Assert.Equal(0, factory.ShowCallCount + factory.CreateCallCount + factory.MergeCallCount + factory.DiffCallCount);
                break;
            default: throw new InvalidOperationException($"Unknown method {method}");
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task VerboseFlag_WiresTraceLevelThroughToLogger(bool verbose)
    {
        var fake = new RecordingOperation(logger =>
        {
            logger.Trace($"trace-line");
            logger.Information($"info-line");
            return Task.FromResult(DatabaseToolsOutcome.Succeeded);
        });
        var (service, _) = CreateSut(showOperation: () => fake);
        var logProgress = new ListProgress<LogRecord>();

        await service.ShowAsync(
            new ShowProvidersRequest(null, null), logProgress, progress: null, CancellationToken.None, verbose: verbose);

        var traceEntries = logProgress.Entries.Where(e => e.Level == LogLevel.Trace).ToList();
        var infoEntries = logProgress.Entries.Where(e => e.Level == LogLevel.Information).ToList();

        if (verbose)
        {
            Assert.Single(traceEntries);
            Assert.Contains("trace-line", traceEntries[0].Message);
        }
        else
        {
            Assert.Empty(traceEntries);
        }

        Assert.Single(infoEntries);
        Assert.Contains("info-line", infoEntries[0].Message);
    }

    private static (DatabaseToolsService Sut, FakeOperationFactory Factory) CreateSut(
        Func<IDatabaseToolsOperation>? showOperation = null,
        Func<IDatabaseToolsOperation>? createOperation = null,
        Func<IDatabaseToolsOperation>? mergeOperation = null,
        Func<IDatabaseToolsOperation>? diffOperation = null,
        Func<IDatabaseToolsOperation>? upgradeOperation = null)
    {
        var factory = new FakeOperationFactory
        {
            ShowFactory = showOperation ?? (() => new RecordingOperation(DatabaseToolsOutcome.Succeeded)),
            CreateFactory = createOperation ?? (() => new RecordingOperation(DatabaseToolsOutcome.Succeeded)),
            MergeFactory = mergeOperation ?? (() => new RecordingOperation(DatabaseToolsOutcome.Succeeded)),
            DiffFactory = diffOperation ?? (() => new RecordingOperation(DatabaseToolsOutcome.Succeeded)),
            UpgradeFactory = upgradeOperation ?? (() => new RecordingOperation(DatabaseToolsOutcome.Succeeded))
        };

        return (new DatabaseToolsService(factory), factory);
    }

    private sealed class FakeOperationFactory : IDatabaseToolsOperationFactory
    {
        public int CreateCallCount { get; private set; }

        public Func<IDatabaseToolsOperation> CreateFactory { get; set; } = () => new RecordingOperation(DatabaseToolsOutcome.Succeeded);

        public int DiffCallCount { get; private set; }

        public Func<IDatabaseToolsOperation> DiffFactory { get; set; } = () => new RecordingOperation(DatabaseToolsOutcome.Succeeded);

        public int MergeCallCount { get; private set; }

        public Func<IDatabaseToolsOperation> MergeFactory { get; set; } = () => new RecordingOperation(DatabaseToolsOutcome.Succeeded);

        public int ShowCallCount { get; private set; }

        public Func<IDatabaseToolsOperation> ShowFactory { get; set; } = () => new RecordingOperation(DatabaseToolsOutcome.Succeeded);

        public int UpgradeCallCount { get; private set; }

        public Func<IDatabaseToolsOperation> UpgradeFactory { get; set; } = () => new RecordingOperation(DatabaseToolsOutcome.Succeeded);

        public IDatabaseToolsOperation Create(ShowProvidersRequest request) { ShowCallCount++; return ShowFactory(); }

        public IDatabaseToolsOperation Create(CreateDatabaseRequest request) { CreateCallCount++; return CreateFactory(); }

        public IDatabaseToolsOperation Create(MergeDatabaseRequest request) { MergeCallCount++; return MergeFactory(); }

        public IDatabaseToolsOperation Create(DiffDatabaseRequest request) { DiffCallCount++; return DiffFactory(); }

        public IDatabaseToolsOperation Create(UpgradeDatabaseRequest request) { UpgradeCallCount++; return UpgradeFactory(); }
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
}
