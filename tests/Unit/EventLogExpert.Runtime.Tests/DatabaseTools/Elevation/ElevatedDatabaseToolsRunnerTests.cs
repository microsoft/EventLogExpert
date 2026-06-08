// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Ipc;
using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.DatabaseTools.CreateDatabase;
using EventLogExpert.DatabaseTools.DiffDatabase;
using EventLogExpert.DatabaseTools.MergeDatabase;
using EventLogExpert.DatabaseTools.ShowProviders;
using EventLogExpert.DatabaseTools.UpgradeDatabase;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.DatabaseTools.Elevation;
using EventLogExpert.Runtime.Tests.DatabaseTools.Elevation.TestUtils;
using EventLogExpert.Runtime.Tests.TestUtils;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Text;
using System.Text.Json;

namespace EventLogExpert.Runtime.Tests.DatabaseTools.Elevation;

public sealed class ElevatedDatabaseToolsRunnerTests
{
    private static readonly TimeSpan s_testExitGrace = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan s_testGrace = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan s_testHelloTimeout = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan s_testReadTimeout = TimeSpan.FromSeconds(5);
    private static readonly UTF8Encoding s_utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    [Fact]
    public async Task CallerCancellation_AfterHello_HelperRespondsWithCancelledResult_RunnerReturnsCancelled()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var pipes = await HelperPipePair.CreateAsync(ct); var server = pipes.Server; var client = pipes.Client;
        var fakeProcess = new FakeElevatedHelperProcess(server, processId: 4242);
        var host = new FakeElevatedHelperProcessHost((_, _) => Task.FromResult<IElevatedHelperProcess>(fakeProcess));
        var logger = new LoggerUtils.RecordingTraceLogger();
        var runner = CreateRunner(host, logger);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var logSink = new ListProgress<LogRecord>();

        await using var clientWriter = new StreamWriter(client, s_utf8NoBom, bufferSize: 4096, leaveOpen: true) { AutoFlush = true };
        using var clientReader = new StreamReader(client, s_utf8NoBom, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);

        var runTask = runner.ShowAsync(
            new ShowProvidersRequest(null, null),
            logSink, progress: null, cts.Token);

        await WriteEnvelopeAsync(clientWriter, new HelloEnvelope(4242, 1), ct);

        var requestEnvelope = await ReadRequestEnvelopeAsync(clientReader, ct);
        Assert.IsType<ShowProvidersIpcRequest>(requestEnvelope);

        cts.Cancel();

        var nextEnvelope = await ReadEnvelopeAsync(clientReader, ct);
        Assert.IsType<CancelEnvelope>(nextEnvelope);

        await WriteEnvelopeAsync(clientWriter, new ResultEnvelope(DatabaseToolsOutcome.Cancelled, "operation cancelled", 100), ct);
        fakeProcess.SignalExited(0);

        var result = await runTask;

        Assert.Equal(DatabaseToolsOutcome.Cancelled, result.Outcome);
        Assert.False(fakeProcess.WasKilled, "Helper responded to cancel cooperatively; force-kill should not fire.");
    }

    [Fact]
    public async Task CallerCancellation_HelperUnresponsive_GraceExpires_RunnerForceKillsAndReturnsCancelled()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var pipes = await HelperPipePair.CreateAsync(ct); var server = pipes.Server; var client = pipes.Client;
        var fakeProcess = new FakeElevatedHelperProcess(server, processId: 5252)
        {
            OnKilled = () => { try { client.Dispose(); } catch { /* test cleanup, best effort */ } }
        };
        var host = new FakeElevatedHelperProcessHost((_, _) => Task.FromResult<IElevatedHelperProcess>(fakeProcess));
        var logger = new LoggerUtils.RecordingTraceLogger();
        var runner = CreateRunner(host, logger);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var logSink = new ListProgress<LogRecord>();

        var clientWriter = new StreamWriter(client, s_utf8NoBom, bufferSize: 4096, leaveOpen: true) { AutoFlush = true };
        var clientReader = new StreamReader(client, s_utf8NoBom, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);

        try
        {
            var runTask = runner.ShowAsync(
                new ShowProvidersRequest(null, null),
                logSink, progress: null, cts.Token);

            await WriteEnvelopeAsync(clientWriter, new HelloEnvelope(5252, 1), ct);

            var requestEnvelope = await ReadRequestEnvelopeAsync(clientReader, ct);
            Assert.IsType<ShowProvidersIpcRequest>(requestEnvelope);

            cts.Cancel();

            var result = await runTask;

            Assert.Equal(DatabaseToolsOutcome.Cancelled, result.Outcome);
            Assert.True(fakeProcess.WasKilled, "Helper failed to respond within grace; runner must force-kill.");
            Assert.NotNull(result.FailureSummary);
            Assert.Contains("force-killed", result.FailureSummary, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DisposeSafely(clientWriter);
            DisposeSafely(clientReader);
        }
    }

    [Fact]
    public async Task HappyPath_AllFiveOperations_RoundTripRequest_ReturnSucceeded()
    {
        await AssertHappyPathOperationAsync(
            (runner, logSink, ct) => runner.CreateAsync(
                new CreateDatabaseRequest(@"C:\out.db", null, null, null), logSink, progress: null, ct, verbose: true),
            expectedRequestType: typeof(CreateDatabaseIpcRequest),
            expectVerbose: true);

        await AssertHappyPathOperationAsync(
            (runner, logSink, ct) => runner.DiffAsync(
                new DiffDatabaseRequest("a.db", "b.db", "diff.db"), logSink, progress: null, ct, verbose: false),
            expectedRequestType: typeof(DiffDatabaseIpcRequest),
            expectVerbose: false);

        await AssertHappyPathOperationAsync(
            (runner, logSink, ct) => runner.MergeAsync(
                new MergeDatabaseRequest("src.db", "tgt.db", true), logSink, progress: null, ct, verbose: false),
            expectedRequestType: typeof(MergeDatabaseIpcRequest),
            expectVerbose: false);

        await AssertHappyPathOperationAsync(
            (runner, logSink, ct) => runner.ShowAsync(
                new ShowProvidersRequest(null, null), logSink, progress: null, ct, verbose: false),
            expectedRequestType: typeof(ShowProvidersIpcRequest),
            expectVerbose: false);

        await AssertHappyPathOperationAsync(
            (runner, logSink, ct) => runner.UpgradeAsync(
                new UpgradeDatabaseRequest("tgt.db"), logSink, progress: null, ct, verbose: false),
            expectedRequestType: typeof(UpgradeDatabaseIpcRequest),
            expectVerbose: false);
    }

    [Fact]
    public async Task HappyPath_StreamsLogAndProgressEnvelopesToCallerSinksInOrder()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var pipes = await HelperPipePair.CreateAsync(ct); var server = pipes.Server; var client = pipes.Client;
        var fakeProcess = new FakeElevatedHelperProcess(server, processId: 9999);
        var host = new FakeElevatedHelperProcessHost((_, _) => Task.FromResult<IElevatedHelperProcess>(fakeProcess));
        var logger = new LoggerUtils.RecordingTraceLogger();
        var runner = CreateRunner(host, logger);

        var logSink = new ListProgress<LogRecord>();
        var progressSink = new ListProgress<DatabaseToolsProgress>();

        await using var clientWriter = new StreamWriter(client, s_utf8NoBom, bufferSize: 4096, leaveOpen: true) { AutoFlush = true };
        using var clientReader = new StreamReader(client, s_utf8NoBom, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);

        var runTask = runner.ShowAsync(
            new ShowProvidersRequest(null, null), logSink, progressSink, ct);

        await WriteEnvelopeAsync(clientWriter, new HelloEnvelope(9999, 1), ct);
        await ReadRequestEnvelopeAsync(clientReader, ct);

        var ts1 = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var ts2 = ts1.AddSeconds(1);

        await WriteEnvelopeAsync(clientWriter, new LogEnvelope(ts1, LogLevel.Information, "first"), ct);
        await WriteEnvelopeAsync(clientWriter, new ProgressEnvelope(1, 10, "item-1"), ct);
        await WriteEnvelopeAsync(clientWriter, new LogEnvelope(ts2, LogLevel.Warning, "second"), ct);
        await WriteEnvelopeAsync(clientWriter, new ResultEnvelope(DatabaseToolsOutcome.Succeeded, null, 250), ct);
        fakeProcess.SignalExited(0);

        var result = await runTask;

        Assert.Equal(DatabaseToolsOutcome.Succeeded, result.Outcome);
        Assert.Equal(2, logSink.Entries.Count);
        Assert.Equal("first", logSink.Entries[0].Message);
        Assert.Equal(LogLevel.Information, logSink.Entries[0].Level);
        Assert.Equal(ts1, logSink.Entries[0].TimestampUtc);
        Assert.Equal("second", logSink.Entries[1].Message);
        Assert.Equal(LogLevel.Warning, logSink.Entries[1].Level);

        Assert.Single(progressSink.Entries);
        Assert.Equal(1, progressSink.Entries[0].Processed);
        Assert.Equal(10, progressSink.Entries[0].Total);
        Assert.Equal("item-1", progressSink.Entries[0].CurrentItem);
    }

    [Fact]
    public async Task HelloEnvelopeWithUnexpectedProtocolVersion_RunnerReturnsFailedWithMismatchMessage()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var pipes = await HelperPipePair.CreateAsync(ct); var server = pipes.Server; var client = pipes.Client;
        var fakeProcess = new FakeElevatedHelperProcess(server, processId: 9090);
        var host = new FakeElevatedHelperProcessHost((_, _) => Task.FromResult<IElevatedHelperProcess>(fakeProcess));
        var logger = new LoggerUtils.RecordingTraceLogger();
        var runner = CreateRunner(host, logger);
        var logSink = new ListProgress<LogRecord>();

        await using var clientWriter = new StreamWriter(client, s_utf8NoBom, bufferSize: 4096, leaveOpen: true) { AutoFlush = true };

        var runTask = runner.ShowAsync(
            new ShowProvidersRequest(null, null), logSink, progress: null, ct);

        var futureVersion = HelloEnvelope.CurrentProtocolVersion + 1;
        await WriteEnvelopeAsync(clientWriter, new HelloEnvelope(9090, futureVersion), ct);
        fakeProcess.SignalExited(0);

        var result = await runTask;

        Assert.Equal(DatabaseToolsOutcome.Failed, result.Outcome);
        Assert.NotNull(result.FailureSummary);
        Assert.Contains("protocol version mismatch", result.FailureSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(futureVersion.ToString(), result.FailureSummary);
        Assert.Contains(HelloEnvelope.CurrentProtocolVersion.ToString(), result.FailureSummary);
    }

    [Fact]
    public async Task HelloTimeout_HelperSilent_RunnerReturnsFailedWithTimeoutMessage()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var pipes = await HelperPipePair.CreateAsync(ct); var server = pipes.Server; var client = pipes.Client;
        var fakeProcess = new FakeElevatedHelperProcess(server, processId: 6262);
        var host = new FakeElevatedHelperProcessHost((_, _) => Task.FromResult<IElevatedHelperProcess>(fakeProcess));
        var logger = new LoggerUtils.RecordingTraceLogger();
        var runner = CreateRunner(host, logger);
        var logSink = new ListProgress<LogRecord>();

        var runTask = runner.ShowAsync(
            new ShowProvidersRequest(null, null), logSink, progress: null, ct);

        fakeProcess.SignalExited(0);

        var result = await runTask;

        Assert.Equal(DatabaseToolsOutcome.Failed, result.Outcome);
        Assert.NotNull(result.FailureSummary);
        Assert.Contains("did not send Hello envelope within", result.FailureSummary);

        await client.DisposeAsync();
    }

    [Fact]
    public async Task HelperExits_BeforeHello_RunnerReturnsFailedWithPipeClosedMessage()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var pipes = await HelperPipePair.CreateAsync(ct); var server = pipes.Server; var client = pipes.Client;
        var fakeProcess = new FakeElevatedHelperProcess(server, processId: 7373);
        var host = new FakeElevatedHelperProcessHost((_, _) => Task.FromResult<IElevatedHelperProcess>(fakeProcess));
        var logger = new LoggerUtils.RecordingTraceLogger();
        var runner = CreateRunner(host, logger);
        var logSink = new ListProgress<LogRecord>();

        var runTask = runner.ShowAsync(
            new ShowProvidersRequest(null, null), logSink, progress: null, ct);

        client.Dispose();
        fakeProcess.SignalExited(99);

        var result = await runTask;

        Assert.Equal(DatabaseToolsOutcome.Failed, result.Outcome);
        Assert.NotNull(result.FailureSummary);
        Assert.Contains("Pipe closed before helper sent Hello", result.FailureSummary);
    }

    [Fact]
    public async Task HelperExits_BetweenHelloAndResult_RunnerReturnsFailedWithExitCodeMessage()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var pipes = await HelperPipePair.CreateAsync(ct); var server = pipes.Server; var client = pipes.Client;
        var fakeProcess = new FakeElevatedHelperProcess(server, processId: 8484);
        var host = new FakeElevatedHelperProcessHost((_, _) => Task.FromResult<IElevatedHelperProcess>(fakeProcess));
        var logger = new LoggerUtils.RecordingTraceLogger();
        var runner = CreateRunner(host, logger);
        var logSink = new ListProgress<LogRecord>();

        var clientWriter = new StreamWriter(client, s_utf8NoBom, bufferSize: 4096, leaveOpen: true) { AutoFlush = true };
        var clientReader = new StreamReader(client, s_utf8NoBom, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);

        try
        {
            var runTask = runner.ShowAsync(
                new ShowProvidersRequest(null, null), logSink, progress: null, ct);

            await WriteEnvelopeAsync(clientWriter, new HelloEnvelope(8484, 1), ct);
            await ReadRequestEnvelopeAsync(clientReader, ct);

            client.Dispose();
            fakeProcess.SignalExited(42);

            var result = await runTask;

            Assert.Equal(DatabaseToolsOutcome.Failed, result.Outcome);
            Assert.NotNull(result.FailureSummary);
            Assert.Contains("exited (code 42) without sending a Result", result.FailureSummary);
        }
        finally
        {
            DisposeSafely(clientWriter);
            DisposeSafely(clientReader);
        }
    }

    [Fact]
    public async Task HelperNotFound_HostThrowsFileNotFound_RunnerReturnsFailedWithNotFoundMessage()
    {
        var ct = TestContext.Current.CancellationToken;
        var host = new FakeElevatedHelperProcessHost((_, _) =>
            throw new FileNotFoundException(@"Could not find eventlogexpert-elevated.exe at expected path."));
        var logger = new LoggerUtils.RecordingTraceLogger();
        var runner = CreateRunner(host, logger);
        var logSink = new ListProgress<LogRecord>();

        var result = await runner.ShowAsync(
            new ShowProvidersRequest(null, null), logSink, progress: null, ct);

        Assert.Equal(DatabaseToolsOutcome.Failed, result.Outcome);
        Assert.NotNull(result.FailureSummary);
        Assert.Contains("Elevation helper not found", result.FailureSummary);
        Assert.Contains("eventlogexpert-elevated.exe", result.FailureSummary);
        Assert.Contains(logger.ErrorMessages, m => m.Contains("Helper executable not found"));
    }

    [Fact]
    public async Task HelperSendsNonHelloFirst_RunnerReturnsFailedWithFirstEnvelopeMessage()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var pipes = await HelperPipePair.CreateAsync(ct); var server = pipes.Server; var client = pipes.Client;
        var fakeProcess = new FakeElevatedHelperProcess(server, processId: 1010);
        var host = new FakeElevatedHelperProcessHost((_, _) => Task.FromResult<IElevatedHelperProcess>(fakeProcess));
        var logger = new LoggerUtils.RecordingTraceLogger();
        var runner = CreateRunner(host, logger);
        var logSink = new ListProgress<LogRecord>();

        await using var clientWriter = new StreamWriter(client, s_utf8NoBom, bufferSize: 4096, leaveOpen: true) { AutoFlush = true };

        var runTask = runner.ShowAsync(
            new ShowProvidersRequest(null, null), logSink, progress: null, ct);

        await WriteEnvelopeAsync(clientWriter,
            new LogEnvelope(DateTime.UtcNow, LogLevel.Information, "premature log"), ct);
        fakeProcess.SignalExited(0);

        var result = await runTask;

        Assert.Equal(DatabaseToolsOutcome.Failed, result.Outcome);
        Assert.NotNull(result.FailureSummary);
        Assert.Contains("LogEnvelope instead of HelloEnvelope", result.FailureSummary);
    }

    [Fact]
    public async Task MalformedJsonLine_SynthesizedAsFatal_RunnerReturnsFailedReferencingJsonException()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var pipes = await HelperPipePair.CreateAsync(ct); var server = pipes.Server; var client = pipes.Client;
        var fakeProcess = new FakeElevatedHelperProcess(server, processId: 1111);
        var host = new FakeElevatedHelperProcessHost((_, _) => Task.FromResult<IElevatedHelperProcess>(fakeProcess));
        var logger = new LoggerUtils.RecordingTraceLogger();
        var runner = CreateRunner(host, logger);
        var logSink = new ListProgress<LogRecord>();

        await using var clientWriter = new StreamWriter(client, s_utf8NoBom, bufferSize: 4096, leaveOpen: true) { AutoFlush = true };
        using var clientReader = new StreamReader(client, s_utf8NoBom, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);

        var runTask = runner.ShowAsync(
            new ShowProvidersRequest(null, null), logSink, progress: null, ct);

        await WriteEnvelopeAsync(clientWriter, new HelloEnvelope(1111, 1), ct);
        await ReadRequestEnvelopeAsync(clientReader, ct);

        await clientWriter.WriteLineAsync("{not valid json");
        await clientWriter.FlushAsync(ct);
        fakeProcess.SignalExited(0);

        var result = await runTask;

        Assert.Equal(DatabaseToolsOutcome.Failed, result.Outcome);
        Assert.NotNull(result.FailureSummary);
        Assert.Contains("Helper threw System.Text.Json.JsonException", result.FailureSummary);
        Assert.Contains("Malformed envelope from helper", result.FailureSummary);
    }

    [Fact]
    public async Task MirrorEnvelopeToDebugLog_LogAndProgressEnvelopes_AreNotMirroredToTraceLogger()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var pipes = await HelperPipePair.CreateAsync(ct); var server = pipes.Server; var client = pipes.Client;
        var fakeProcess = new FakeElevatedHelperProcess(server, processId: 2222);
        var host = new FakeElevatedHelperProcessHost((_, _) => Task.FromResult<IElevatedHelperProcess>(fakeProcess));
        var logger = new LoggerUtils.RecordingTraceLogger();
        var runner = CreateRunner(host, logger);
        var logSink = new ListProgress<LogRecord>();
        var progressSink = new ListProgress<DatabaseToolsProgress>();

        await using var clientWriter = new StreamWriter(client, s_utf8NoBom, bufferSize: 4096, leaveOpen: true) { AutoFlush = true };
        using var clientReader = new StreamReader(client, s_utf8NoBom, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);

        var runTask = runner.ShowAsync(
            new ShowProvidersRequest(null, null), logSink, progressSink, ct);

        await WriteEnvelopeAsync(clientWriter, new HelloEnvelope(2222, 1), ct);
        await ReadRequestEnvelopeAsync(clientReader, ct);

        for (var i = 0; i < 20; i++)
        {
            await WriteEnvelopeAsync(clientWriter,
                new LogEnvelope(DateTime.UtcNow, LogLevel.Information, $"OPERATION_LOG_LINE_{i:000}"), ct);
            await WriteEnvelopeAsync(clientWriter,
                new ProgressEnvelope(i, 20, $"OPERATION_PROGRESS_ITEM_{i:000}"), ct);
        }

        await WriteEnvelopeAsync(clientWriter, new ResultEnvelope(DatabaseToolsOutcome.Succeeded, null, 100), ct);
        fakeProcess.SignalExited(0);

        var result = await runTask;

        Assert.Equal(DatabaseToolsOutcome.Succeeded, result.Outcome);
        Assert.Equal(20, logSink.Entries.Count);
        Assert.Equal(20, progressSink.Entries.Count);

        var allTraceMessages = logger.TraceMessages
            .Concat(logger.DebugMessages)
            .Concat(logger.InfoMessages)
            .Concat(logger.WarnMessages)
            .Concat(logger.ErrorMessages)
            .Concat(logger.CriticalMessages)
            .ToList();

        Assert.DoesNotContain(allTraceMessages, m => m.Contains("OPERATION_LOG_LINE_"));
        Assert.DoesNotContain(allTraceMessages, m => m.Contains("OPERATION_PROGRESS_ITEM_"));
        Assert.Contains(logger.TraceMessages, m => m.Contains("Hello") && m.Contains("2222"));
        Assert.Contains(logger.TraceMessages, m => m.Contains("Result: Succeeded"));
    }

    [Fact]
    public async Task PreCancelledToken_HostObservesCancellation_RunnerReturnsCancelled()
    {
        var ct = TestContext.Current.CancellationToken;
        using var preCancelledCts = new CancellationTokenSource();
        preCancelledCts.Cancel();

        var host = new FakeElevatedHelperProcessHost((_, hostCt) =>
        {
            hostCt.ThrowIfCancellationRequested();
            throw new InvalidOperationException("host should not reach this line");
        });
        var logger = new LoggerUtils.RecordingTraceLogger();
        var runner = CreateRunner(host, logger);
        var logSink = new ListProgress<LogRecord>();

        var result = await runner.ShowAsync(
            new ShowProvidersRequest(null, null), logSink, progress: null, preCancelledCts.Token);

        Assert.Equal(DatabaseToolsOutcome.Cancelled, result.Outcome);
        Assert.Equal("Cancelled before helper spawn completed.", result.FailureSummary);
    }

    [Fact]
    public async Task SinkThrows_SafeReportSwallows_RunnerCompletesSucceeded()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var pipes = await HelperPipePair.CreateAsync(ct); var server = pipes.Server; var client = pipes.Client;
        var fakeProcess = new FakeElevatedHelperProcess(server, processId: 3333);
        var host = new FakeElevatedHelperProcessHost((_, _) => Task.FromResult<IElevatedHelperProcess>(fakeProcess));
        var logger = new LoggerUtils.RecordingTraceLogger();
        var runner = CreateRunner(host, logger);
        var throwingSink = new ThrowingProgress<LogRecord>("sink-boom");

        await using var clientWriter = new StreamWriter(client, s_utf8NoBom, bufferSize: 4096, leaveOpen: true) { AutoFlush = true };
        using var clientReader = new StreamReader(client, s_utf8NoBom, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);

        var runTask = runner.ShowAsync(
            new ShowProvidersRequest(null, null), throwingSink, progress: null, ct);

        await WriteEnvelopeAsync(clientWriter, new HelloEnvelope(3333, 1), ct);
        await ReadRequestEnvelopeAsync(clientReader, ct);

        await WriteEnvelopeAsync(clientWriter,
            new LogEnvelope(DateTime.UtcNow, LogLevel.Information, "boom-trigger"), ct);
        await WriteEnvelopeAsync(clientWriter, new ResultEnvelope(DatabaseToolsOutcome.Succeeded, null, 100), ct);
        fakeProcess.SignalExited(0);

        var result = await runTask;

        Assert.Equal(DatabaseToolsOutcome.Succeeded, result.Outcome);
        Assert.Contains(logger.WarnMessages, m => m.Contains("IProgress") && m.Contains("InvalidOperationException") && m.Contains("sink-boom"));
    }

    [Fact]
    public async Task UacDeclined_HostThrowsWin32Exception1223_RunnerReturnsCancelledWithDeclineMessage()
    {
        var ct = TestContext.Current.CancellationToken;
        var host = new FakeElevatedHelperProcessHost((_, _) =>
            throw new Win32Exception(1223, "The operation was canceled by the user."));
        var logger = new LoggerUtils.RecordingTraceLogger();
        var runner = CreateRunner(host, logger);
        var logSink = new ListProgress<LogRecord>();

        var result = await runner.ShowAsync(
            new ShowProvidersRequest(null, null), logSink, progress: null, ct);

        Assert.Equal(DatabaseToolsOutcome.Cancelled, result.Outcome);
        Assert.Equal("User declined the UAC prompt.", result.FailureSummary);
        Assert.Contains(logger.InfoMessages, m => m.Contains("declined the UAC prompt"));
    }

    [Fact]
    public async Task VerboseFlag_PropagatesIntoRequestEnvelopeSentToHelper()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var pipes = await HelperPipePair.CreateAsync(ct); var server = pipes.Server; var client = pipes.Client;
        var fakeProcess = new FakeElevatedHelperProcess(server, processId: 4444);
        var host = new FakeElevatedHelperProcessHost((_, _) => Task.FromResult<IElevatedHelperProcess>(fakeProcess));
        var logger = new LoggerUtils.RecordingTraceLogger();
        var runner = CreateRunner(host, logger);
        var logSink = new ListProgress<LogRecord>();

        await using var clientWriter = new StreamWriter(client, s_utf8NoBom, bufferSize: 4096, leaveOpen: true) { AutoFlush = true };
        using var clientReader = new StreamReader(client, s_utf8NoBom, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);

        var runTask = runner.CreateAsync(
            new CreateDatabaseRequest(@"C:\out.db", null, null, null),
            logSink, progress: null, ct, verbose: true);

        await WriteEnvelopeAsync(clientWriter, new HelloEnvelope(4444, 1), ct);

        var requestEnvelope = await ReadRequestEnvelopeAsync(clientReader, ct);
        var create = Assert.IsType<CreateDatabaseIpcRequest>(requestEnvelope);
        Assert.True(create.Verbose);
        Assert.Equal(@"C:\out.db", create.Request.TargetPath);

        await WriteEnvelopeAsync(clientWriter, new ResultEnvelope(DatabaseToolsOutcome.Succeeded, null, 50), ct);
        fakeProcess.SignalExited(0);

        var result = await runTask;
        Assert.Equal(DatabaseToolsOutcome.Succeeded, result.Outcome);
    }

    private static async Task AssertHappyPathOperationAsync(
        Func<IElevatedDatabaseToolsRunner, ListProgress<LogRecord>, CancellationToken, Task<DatabaseToolsResult>> invoke,
        Type expectedRequestType,
        bool expectVerbose)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var pipes = await HelperPipePair.CreateAsync(ct); var server = pipes.Server; var client = pipes.Client;
        var fakeProcess = new FakeElevatedHelperProcess(server, processId: 1234);
        var host = new FakeElevatedHelperProcessHost((_, _) => Task.FromResult<IElevatedHelperProcess>(fakeProcess));
        var logger = new LoggerUtils.RecordingTraceLogger();
        var runner = CreateRunner(host, logger);
        var logSink = new ListProgress<LogRecord>();

        await using var clientWriter = new StreamWriter(client, s_utf8NoBom, bufferSize: 4096, leaveOpen: true) { AutoFlush = true };
        using var clientReader = new StreamReader(client, s_utf8NoBom, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);

        var runTask = invoke(runner, logSink, ct);

        await WriteEnvelopeAsync(clientWriter, new HelloEnvelope(1234, 1), ct);

        var requestEnvelope = await ReadRequestEnvelopeAsync(clientReader, ct);
        Assert.IsType(expectedRequestType, requestEnvelope);
        Assert.Equal(expectVerbose, requestEnvelope.Verbose);

        await WriteEnvelopeAsync(clientWriter, new ResultEnvelope(DatabaseToolsOutcome.Succeeded, null, 100), ct);
        fakeProcess.SignalExited(0);

        var result = await runTask;
        Assert.Equal(DatabaseToolsOutcome.Succeeded, result.Outcome);
        Assert.Null(result.FailureSummary);
    }

    private static ElevatedDatabaseToolsRunner CreateRunner(IElevatedHelperProcessHost host, ITraceLogger logger) =>
        new(host, logger, s_testHelloTimeout, s_testGrace, s_testExitGrace);

    private static void DisposeSafely(IDisposable disposable)
    {
        try { disposable.Dispose(); }
        catch (ObjectDisposedException) { /* underlying pipe was closed by OnKilled callback */ }
        catch (IOException) { /* pipe broke during flush-on-dispose */ }
    }

    private static async Task<DatabaseToolsIpcEnvelope> ReadEnvelopeAsync(StreamReader reader, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(s_testReadTimeout);

        var line = await reader.ReadLineAsync(timeoutCts.Token);
        Assert.False(string.IsNullOrEmpty(line), "Expected an envelope but the pipe returned EOF or empty.");

        var envelope = JsonSerializer.Deserialize<DatabaseToolsIpcEnvelope>(line, DatabaseToolsIpcSerializer.Options);
        Assert.NotNull(envelope);
        return envelope;
    }

    private static async Task<DatabaseToolsIpcRequest> ReadRequestEnvelopeAsync(StreamReader reader, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(s_testReadTimeout);

        var line = await reader.ReadLineAsync(timeoutCts.Token);
        Assert.False(string.IsNullOrEmpty(line), "Expected a request envelope but the pipe returned EOF or empty.");

        var request = JsonSerializer.Deserialize<DatabaseToolsIpcRequest>(line, DatabaseToolsIpcSerializer.Options);
        Assert.NotNull(request);
        return request;
    }

    private static async Task WriteEnvelopeAsync(StreamWriter writer, DatabaseToolsIpcEnvelope envelope, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(envelope, DatabaseToolsIpcSerializer.Options);
        await writer.WriteLineAsync(json.AsMemory(), ct);
        await writer.FlushAsync(ct);
    }
}
