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

        await WriteMessageAsync(clientWriter, new HelloMessage(4242, 1), ct);

        var request = await ReadRequestAsync(clientReader, ct);
        Assert.IsType<ShowProvidersIpcRequest>(request);

        cts.Cancel();

        var nextMessage = await ReadMessageAsync(clientReader, ct);
        Assert.IsType<CancelMessage>(nextMessage);

        await WriteMessageAsync(clientWriter, new ResultMessage(DatabaseToolsOutcome.Cancelled, "operation cancelled", 100), ct);
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

            await WriteMessageAsync(clientWriter, new HelloMessage(5252, 1), ct);

            var request = await ReadRequestAsync(clientReader, ct);
            Assert.IsType<ShowProvidersIpcRequest>(request);

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
    public async Task HappyPath_StreamsLogAndProgressMessagesToCallerSinksInOrder()
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

        await WriteMessageAsync(clientWriter, new HelloMessage(9999, 1), ct);
        await ReadRequestAsync(clientReader, ct);

        var ts1 = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var ts2 = ts1.AddSeconds(1);

        await WriteMessageAsync(clientWriter, new LogMessage(ts1, LogLevel.Information, "first"), ct);
        await WriteMessageAsync(clientWriter, new ProgressMessage(1, 10, "item-1"), ct);
        await WriteMessageAsync(clientWriter, new LogMessage(ts2, LogLevel.Warning, "second"), ct);
        await WriteMessageAsync(clientWriter, new ResultMessage(DatabaseToolsOutcome.Succeeded, null, 250), ct);
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
    public async Task HelloMessageWithUnexpectedProtocolVersion_RunnerReturnsFailedWithMismatchMessage()
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

        var futureVersion = HelloMessage.CurrentProtocolVersion + 1;
        await WriteMessageAsync(clientWriter, new HelloMessage(9090, futureVersion), ct);
        fakeProcess.SignalExited(0);

        var result = await runTask;

        Assert.Equal(DatabaseToolsOutcome.Failed, result.Outcome);
        Assert.NotNull(result.FailureSummary);
        Assert.Contains("protocol version mismatch", result.FailureSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(futureVersion.ToString(), result.FailureSummary);
        Assert.Contains(HelloMessage.CurrentProtocolVersion.ToString(), result.FailureSummary);
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
        Assert.Contains("did not send Hello message within", result.FailureSummary);

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

            await WriteMessageAsync(clientWriter, new HelloMessage(8484, 1), ct);
            await ReadRequestAsync(clientReader, ct);

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
    public async Task HelperSendsNonHelloFirst_RunnerReportsFirstMessageType()
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

        await WriteMessageAsync(clientWriter,
            new LogMessage(DateTime.UtcNow, LogLevel.Information, "premature log"), ct);
        fakeProcess.SignalExited(0);

        var result = await runTask;

        Assert.Equal(DatabaseToolsOutcome.Failed, result.Outcome);
        Assert.NotNull(result.FailureSummary);
        Assert.Contains("LogMessage instead of HelloMessage", result.FailureSummary);
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

        await WriteMessageAsync(clientWriter, new HelloMessage(1111, 1), ct);
        await ReadRequestAsync(clientReader, ct);

        await clientWriter.WriteLineAsync("{not valid json");
        await clientWriter.FlushAsync(ct);
        fakeProcess.SignalExited(0);

        var result = await runTask;

        Assert.Equal(DatabaseToolsOutcome.Failed, result.Outcome);
        Assert.NotNull(result.FailureSummary);
        Assert.Contains("Helper threw System.Text.Json.JsonException", result.FailureSummary);
        Assert.Contains("Malformed message from helper", result.FailureSummary);
    }

    [Fact]
    public async Task MirrorMessageToDebugLog_LogAndProgressMessages_AreNotMirroredToTraceLogger()
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

        await WriteMessageAsync(clientWriter, new HelloMessage(2222, 1), ct);
        await ReadRequestAsync(clientReader, ct);

        for (var i = 0; i < 20; i++)
        {
            await WriteMessageAsync(clientWriter,
                new LogMessage(DateTime.UtcNow, LogLevel.Information, $"OPERATION_LOG_LINE_{i:000}"), ct);
            await WriteMessageAsync(clientWriter,
                new ProgressMessage(i, 20, $"OPERATION_PROGRESS_ITEM_{i:000}"), ct);
        }

        await WriteMessageAsync(clientWriter, new ResultMessage(DatabaseToolsOutcome.Succeeded, null, 100), ct);
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
    public async Task RunAsync_WhenHelloTimeoutAndHelperUnkillable_CentralizedFinallyCleansUpWithinBoundedTime()
    {
        // Early-return path: helper spawns but never sends Hello, then proves unkillable. The centralized
        // finally must: cancel reader, await pipeReader task (bounded), dispose pipe (graceful), bounded wait,
        // fallback Kill (returns false), exit. Must not hang.
        var ct = TestContext.Current.CancellationToken;
        await using var pipes = await HelperPipePair.CreateAsync(ct); var server = pipes.Server; var client = pipes.Client;
        var fakeProcess = new FakeElevatedHelperProcess(server, processId: 4242)
        {
            SimulateUnkillable = true
        };
        var host = new FakeElevatedHelperProcessHost((_, _) => Task.FromResult<IElevatedHelperProcess>(fakeProcess));
        var logger = new LoggerUtils.RecordingTraceLogger();
        var runner = CreateRunner(host, logger);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var logSink = new ListProgress<LogRecord>();

        var runTask = runner.ShowAsync(
            new ShowProvidersRequest(null, null),
            logSink, progress: null, cts.Token);

        // Don't send Hello — let _helloTimeout (500ms) fire to trigger the early-return path. Centralized
        // finally must clean up without hanging. Ceiling: hello-timeout + 2*exit-grace ≈ 1.5s. 10s slack.
        var result = await runTask.WaitAsync(TimeSpan.FromSeconds(10), ct);

        Assert.Equal(DatabaseToolsOutcome.Failed, result.Outcome);
        Assert.Contains("Hello", result.FailureSummary, StringComparison.OrdinalIgnoreCase);
        Assert.True(fakeProcess.WasKilled);
    }

    [Fact]
    public async Task RunAsync_WhenHelperKillSucceedsAfterCancel_ReportsForceKilled()
    {
        // Companion to the unkillable test: when Kill returns true (succeeds), Disposition=Succeeded and
        // TranslateOutcome emits the existing "force-killed" message (NOT the orphan message).
        var ct = TestContext.Current.CancellationToken;
        await using var pipes = await HelperPipePair.CreateAsync(ct); var server = pipes.Server; var client = pipes.Client;
        var fakeProcess = new FakeElevatedHelperProcess(server, processId: 4242)
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

            await WriteMessageAsync(clientWriter, new HelloMessage(4242, 1), ct);

            var request = await ReadRequestAsync(clientReader, ct);
            Assert.IsType<ShowProvidersIpcRequest>(request);

            cts.Cancel();

            var result = await runTask.WaitAsync(TimeSpan.FromSeconds(10), ct);

            Assert.Equal(DatabaseToolsOutcome.Cancelled, result.Outcome);
            Assert.Contains("force-killed", result.FailureSummary, StringComparison.OrdinalIgnoreCase);
            Assert.True(fakeProcess.WasKilled);
        }
        finally
        {
            DisposeSafely(clientWriter);
            DisposeSafely(clientReader);
        }
    }

    [Fact]
    public async Task RunAsync_WhenHelperUnkillableAfterCancel_DoesNotHangAndReportsOrphan()
    {
        // Original bug: a helper that ignores CancelMessage AND can't be force-killed (medium-IL runner trying
        // to kill a high-IL helper). Before the fix, the runner deadlocked on the unbounded WaitForExitAsync.
        // After the fix, the kill-timer detects Kill returned false, disposes the pipe to unblock the drain
        // loop, marks Disposition=Failed, and TranslateOutcome reports the orphan helper to the caller.
        var ct = TestContext.Current.CancellationToken;
        await using var pipes = await HelperPipePair.CreateAsync(ct); var server = pipes.Server; var client = pipes.Client;
        var fakeProcess = new FakeElevatedHelperProcess(server, processId: 4242)
        {
            SimulateUnkillable = true
        };
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

        await WriteMessageAsync(clientWriter, new HelloMessage(4242, 1), ct);

        var request = await ReadRequestAsync(clientReader, ct);
        Assert.IsType<ShowProvidersIpcRequest>(request);

        cts.Cancel();

        // Ceiling: cancellation-grace (~500ms) + exit-grace (~500ms) for post-IPC bounded wait. 10s gives generous slack.
        var result = await runTask.WaitAsync(TimeSpan.FromSeconds(10), ct);

        Assert.Equal(DatabaseToolsOutcome.Cancelled, result.Outcome);
        Assert.Contains("could not be terminated", result.FailureSummary, StringComparison.OrdinalIgnoreCase);
        Assert.True(fakeProcess.WasKilled);
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

        await WriteMessageAsync(clientWriter, new HelloMessage(3333, 1), ct);
        await ReadRequestAsync(clientReader, ct);

        await WriteMessageAsync(clientWriter,
            new LogMessage(DateTime.UtcNow, LogLevel.Information, "boom-trigger"), ct);
        await WriteMessageAsync(clientWriter, new ResultMessage(DatabaseToolsOutcome.Succeeded, null, 100), ct);
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
    public async Task VerboseFlag_PropagatesIntoRequestSentToHelper()
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

        await WriteMessageAsync(clientWriter, new HelloMessage(4444, 1), ct);

        var request = await ReadRequestAsync(clientReader, ct);
        var create = Assert.IsType<CreateDatabaseIpcRequest>(request);
        Assert.True(create.Verbose);
        Assert.Equal(@"C:\out.db", create.Request.TargetPath);

        await WriteMessageAsync(clientWriter, new ResultMessage(DatabaseToolsOutcome.Succeeded, null, 50), ct);
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

        await WriteMessageAsync(clientWriter, new HelloMessage(1234, 1), ct);

        var request = await ReadRequestAsync(clientReader, ct);
        Assert.IsType(expectedRequestType, request);
        Assert.Equal(expectVerbose, request.Verbose);

        await WriteMessageAsync(clientWriter, new ResultMessage(DatabaseToolsOutcome.Succeeded, null, 100), ct);
        fakeProcess.SignalExited(0);

        var result = await runTask;
        Assert.Equal(DatabaseToolsOutcome.Succeeded, result.Outcome);
        Assert.Null(result.FailureSummary);
    }

    [Fact]
    public async Task RunAsync_WhenProtocolMismatchAndHelperUnkillable_CentralizedFinallyCleansUpWithinBoundedTime()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var pipes = await HelperPipePair.CreateAsync(ct); var server = pipes.Server; var client = pipes.Client;
        var fakeProcess = new FakeElevatedHelperProcess(server, processId: 4242)
        {
            SimulateUnkillable = true
        };
        var host = new FakeElevatedHelperProcessHost((_, _) => Task.FromResult<IElevatedHelperProcess>(fakeProcess));
        var logger = new LoggerUtils.RecordingTraceLogger();
        var runner = CreateRunner(host, logger);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var logSink = new ListProgress<LogRecord>();

        await using var clientWriter = new StreamWriter(client, s_utf8NoBom, bufferSize: 4096, leaveOpen: true) { AutoFlush = true };

        var runTask = runner.ShowAsync(
            new ShowProvidersRequest(null, null),
            logSink, progress: null, cts.Token);

        // Send a Hello with mismatched protocol version to trigger the protocol-mismatch early return.
        await WriteMessageAsync(clientWriter, new HelloMessage(4242, ProtocolVersion: 99), ct);

        var result = await runTask.WaitAsync(TimeSpan.FromSeconds(10), ct);

        Assert.Equal(DatabaseToolsOutcome.Failed, result.Outcome);
        Assert.Contains("protocol version", result.FailureSummary, StringComparison.OrdinalIgnoreCase);
        Assert.True(fakeProcess.WasKilled);
    }

    [Fact]
    public async Task RunAsync_WhenFirstEnvelopeIsNotHelloAndHelperUnkillable_CentralizedFinallyCleansUpWithinBoundedTime()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var pipes = await HelperPipePair.CreateAsync(ct); var server = pipes.Server; var client = pipes.Client;
        var fakeProcess = new FakeElevatedHelperProcess(server, processId: 4242)
        {
            SimulateUnkillable = true
        };
        var host = new FakeElevatedHelperProcessHost((_, _) => Task.FromResult<IElevatedHelperProcess>(fakeProcess));
        var logger = new LoggerUtils.RecordingTraceLogger();
        var runner = CreateRunner(host, logger);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var logSink = new ListProgress<LogRecord>();

        await using var clientWriter = new StreamWriter(client, s_utf8NoBom, bufferSize: 4096, leaveOpen: true) { AutoFlush = true };

        var runTask = runner.ShowAsync(
            new ShowProvidersRequest(null, null),
            logSink, progress: null, cts.Token);

        // Send a non-Hello message first to trigger the wrong-first-envelope early return.
        await WriteMessageAsync(clientWriter, new LogMessage(DateTime.UtcNow, LogLevel.Information, "early log"), ct);

        var result = await runTask.WaitAsync(TimeSpan.FromSeconds(10), ct);

        Assert.Equal(DatabaseToolsOutcome.Failed, result.Outcome);
        Assert.Contains("HelloMessage", result.FailureSummary);
        Assert.True(fakeProcess.WasKilled);
    }

    [Fact]
    public async Task RunAsync_WhenCancelledBeforeHandshakeAndHelperUnkillable_CentralizedFinallyCleansUpWithinBoundedTime()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var pipes = await HelperPipePair.CreateAsync(ct); var server = pipes.Server; var client = pipes.Client;
        var fakeProcess = new FakeElevatedHelperProcess(server, processId: 4242)
        {
            SimulateUnkillable = true
        };
        var host = new FakeElevatedHelperProcessHost((_, _) => Task.FromResult<IElevatedHelperProcess>(fakeProcess));
        var logger = new LoggerUtils.RecordingTraceLogger();
        var runner = CreateRunner(host, logger);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var logSink = new ListProgress<LogRecord>();

        var runTask = runner.ShowAsync(
            new ShowProvidersRequest(null, null),
            logSink, progress: null, cts.Token);

        // Cancel before Hello arrives — give the runner a beat to enter the Hello wait, then cancel.
        await Task.Delay(50, ct);
        cts.Cancel();

        var result = await runTask.WaitAsync(TimeSpan.FromSeconds(10), ct);

        Assert.Equal(DatabaseToolsOutcome.Cancelled, result.Outcome);
        Assert.True(fakeProcess.WasKilled);
    }

    [Fact]
    public async Task RunAsync_WhenPipeClosedBeforeHelloAndHelperUnkillable_CentralizedFinallyCleansUpWithinBoundedTime()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var pipes = await HelperPipePair.CreateAsync(ct); var server = pipes.Server; var client = pipes.Client;
        var fakeProcess = new FakeElevatedHelperProcess(server, processId: 4242)
        {
            SimulateUnkillable = true
        };
        var host = new FakeElevatedHelperProcessHost((_, _) => Task.FromResult<IElevatedHelperProcess>(fakeProcess));
        var logger = new LoggerUtils.RecordingTraceLogger();
        var runner = CreateRunner(host, logger);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var logSink = new ListProgress<LogRecord>();

        var runTask = runner.ShowAsync(
            new ShowProvidersRequest(null, null),
            logSink, progress: null, cts.Token);

        // Close the helper-side pipe immediately to trigger the pipe-closed-before-Hello path.
        await Task.Delay(50, ct);
        await client.DisposeAsync();

        var result = await runTask.WaitAsync(TimeSpan.FromSeconds(10), ct);

        Assert.Equal(DatabaseToolsOutcome.Failed, result.Outcome);
        Assert.Contains("Pipe closed", result.FailureSummary, StringComparison.OrdinalIgnoreCase);
        Assert.True(fakeProcess.WasKilled);
    }

    private static ElevatedDatabaseToolsRunner CreateRunner(IElevatedHelperProcessHost host, ITraceLogger logger) =>
        new(host, logger, s_testHelloTimeout, s_testGrace, s_testExitGrace);

    private static void DisposeSafely(IDisposable disposable)
    {
        try { disposable.Dispose(); }
        catch (ObjectDisposedException) { /* underlying pipe was closed by OnKilled callback */ }
        catch (IOException) { /* pipe broke during flush-on-dispose */ }
    }

    private static async Task<DatabaseToolsIpcMessage> ReadMessageAsync(StreamReader reader, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(s_testReadTimeout);

        var line = await reader.ReadLineAsync(timeoutCts.Token);
        Assert.False(string.IsNullOrEmpty(line), "Expected a message but the pipe returned EOF or empty.");

        var message = JsonSerializer.Deserialize<DatabaseToolsIpcMessage>(line, DatabaseToolsIpcSerializer.Options);
        Assert.NotNull(message);
        return message;
    }

    private static async Task<DatabaseToolsIpcRequest> ReadRequestAsync(StreamReader reader, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(s_testReadTimeout);

        var line = await reader.ReadLineAsync(timeoutCts.Token);
        Assert.False(string.IsNullOrEmpty(line), "Expected a request but the pipe returned EOF or empty.");

        var request = JsonSerializer.Deserialize<DatabaseToolsIpcRequest>(line, DatabaseToolsIpcSerializer.Options);
        Assert.NotNull(request);
        return request;
    }

    private static async Task WriteMessageAsync(StreamWriter writer, DatabaseToolsIpcMessage message, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(message, DatabaseToolsIpcSerializer.Options);
        await writer.WriteLineAsync(json.AsMemory(), ct);
        await writer.FlushAsync(ct);
    }
}
