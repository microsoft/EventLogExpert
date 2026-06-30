// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Ipc;
using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.DatabaseTools.CreateDatabase;
using EventLogExpert.DatabaseTools.DiffDatabase;
using EventLogExpert.DatabaseTools.MergeDatabase;
using EventLogExpert.DatabaseTools.ShowProviders;
using EventLogExpert.DatabaseTools.UpgradeDatabase;
using EventLogExpert.Eventing.PublisherMetadata.Offline;
using EventLogExpert.Logging.Abstractions;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace EventLogExpert.Runtime.DatabaseTools.Elevation;

// Duplex named-pipe buffers permit concurrent drain reads and request/cancel writes.
internal sealed class ElevatedDatabaseToolsRunner : IElevatedDatabaseToolsRunner
{
    internal const string ElevatedHelperTag = "[ElevatedHelper]";

    private const int ChannelCapacity = 1024;

    private static readonly UTF8Encoding s_utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly TimeSpan _cancellationGrace;
    private readonly TimeSpan _exitGrace;
    private readonly TimeSpan _helloTimeout;
    private readonly IElevatedHelperProcessHost _host;
    private readonly ITraceLogger _traceLogger;

    public ElevatedDatabaseToolsRunner(IElevatedHelperProcessHost host, ITraceLogger traceLogger)
        : this(host, traceLogger, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)) { }

    internal ElevatedDatabaseToolsRunner(
        IElevatedHelperProcessHost host,
        ITraceLogger traceLogger,
        TimeSpan helloTimeout,
        TimeSpan cancellationGrace,
        TimeSpan exitGrace)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(traceLogger);

        if (helloTimeout <= TimeSpan.Zero) { throw new ArgumentOutOfRangeException(nameof(helloTimeout), helloTimeout, "Must be positive."); }

        if (cancellationGrace <= TimeSpan.Zero) { throw new ArgumentOutOfRangeException(nameof(cancellationGrace), cancellationGrace, "Must be positive."); }

        if (exitGrace <= TimeSpan.Zero) { throw new ArgumentOutOfRangeException(nameof(exitGrace), exitGrace, "Must be positive."); }

        _host = host;
        _traceLogger = traceLogger;
        _helloTimeout = helloTimeout;
        _cancellationGrace = cancellationGrace;
        _exitGrace = exitGrace;
    }

    private enum KillDisposition
    {
        NotAttempted = 0,
        Succeeded = 1,
        Failed = 2
    }

    public Task<DatabaseToolsResult> CreateAsync(
        CreateDatabaseRequest request,
        IProgress<LogRecord> logSink,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken,
        bool verbose = false) =>
        RunAsync(new CreateDatabaseIpcRequest(request, verbose), logSink, progress, cancellationToken);

    public Task<DatabaseToolsResult> DiffAsync(
        DiffDatabaseRequest request,
        IProgress<LogRecord> logSink,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken,
        bool verbose = false) =>
        RunAsync(new DiffDatabaseIpcRequest(request, verbose), logSink, progress, cancellationToken);

    public async Task<OfflineImageEditionsResult> ListImageEditionsAsync(
        ListOfflineImageEditionsRequest request,
        IProgress<LogRecord> logSink,
        CancellationToken cancellationToken,
        bool verbose = false)
    {
        ArgumentNullException.ThrowIfNull(request);

        ImageEditionsMessage? editions = null;

        var result = await RunAsync(
            new ListImageEditionsIpcRequest(request, verbose),
            logSink,
            progressSink: null,
            cancellationToken,
            onDataMessage: message =>
            {
                if (message is ImageEditionsMessage imageEditions) { editions = imageEditions; }
            });

        if (result.Outcome != DatabaseToolsOutcome.Succeeded)
        {
            return new OfflineImageEditionsResult(result.Outcome, null, result.FailureSummary);
        }

        if (editions is null)
        {
            return new OfflineImageEditionsResult(
                DatabaseToolsOutcome.Failed,
                null,
                "The elevation helper reported success but did not return the image editions.");
        }

        return new OfflineImageEditionsResult(
            DatabaseToolsOutcome.Succeeded,
            new WimImageList(editions.Status, editions.Images),
            null);
    }

    public Task<DatabaseToolsResult> MergeAsync(
        MergeDatabaseRequest request,
        IProgress<LogRecord> logSink,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken,
        bool verbose = false) =>
        RunAsync(new MergeDatabaseIpcRequest(request, verbose), logSink, progress, cancellationToken);

    public Task<DatabaseToolsResult> ShowAsync(
        ShowProvidersRequest request,
        IProgress<LogRecord> logSink,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken,
        bool verbose = false) =>
        RunAsync(new ShowProvidersIpcRequest(request, verbose), logSink, progress, cancellationToken);

    public Task<DatabaseToolsResult> UpgradeAsync(
        UpgradeDatabaseRequest request,
        IProgress<LogRecord> logSink,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken,
        bool verbose = false) =>
        RunAsync(new UpgradeDatabaseIpcRequest(request, verbose), logSink, progress, cancellationToken);

    private static async Task DrainPipeAsync(Stream pipe, ChannelWriter<DatabaseToolsIpcMessage> writer, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(pipe, s_utf8NoBom, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string? line;

                try
                {
                    line = await reader.ReadLineAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (IOException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                if (line is null) { break; }

                if (string.IsNullOrWhiteSpace(line)) { continue; }

                DatabaseToolsIpcMessage message;

                try
                {
                    message = JsonSerializer.Deserialize<DatabaseToolsIpcMessage>(line, DatabaseToolsIpcSerializer.Options)
                        ?? throw new JsonException("Deserialized message was null.");
                }
                catch (Exception ex)
                {
                    message = new FatalMessage(
                        ex.GetType().FullName ?? ex.GetType().Name,
                        $"Malformed message from helper: {ex.Message} (line: {Truncate(line, 200)})",
                        ex.StackTrace ?? string.Empty);
                }

                try
                {
                    await writer.WriteAsync(message, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ChannelClosedException)
                {
                    break;
                }
            }
        }
        finally
        {
            writer.TryComplete();
        }
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "...";

    private static async Task WriteJsonLineAsync(Stream pipe, SemaphoreSlim writeLock, string json, CancellationToken cancellationToken)
    {
        var payload = s_utf8NoBom.GetBytes(json + "\n");

        await writeLock.WaitAsync(cancellationToken);

        try
        {
            await pipe.WriteAsync(payload, cancellationToken);
            await pipe.FlushAsync(cancellationToken);
        }
        finally
        {
            writeLock.Release();
        }
    }

    private static async Task WriteMessageAsync(Stream pipe, SemaphoreSlim writeLock, DatabaseToolsIpcMessage message, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(message, DatabaseToolsIpcSerializer.Options);

        await WriteJsonLineAsync(pipe, writeLock, json, cancellationToken);
    }

    private static async Task WriteRequestAsync(Stream pipe, SemaphoreSlim writeLock, DatabaseToolsIpcRequest request, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(request, DatabaseToolsIpcSerializer.Options);

        await WriteJsonLineAsync(pipe, writeLock, json, cancellationToken);
    }

    private void HandleCallerCancellation(Stream pipeStream, SemaphoreSlim writeLock, IElevatedHelperProcess process, KillState killState)
    {
        if (!killState.MarkCancelRequested()) { return; }

        _traceLogger.Information($"{ElevatedHelperTag} Caller cancellation requested; sending CancelMessage to helper and starting {_cancellationGrace.TotalSeconds:N0}s grace window.");

        _ = Task.Run(async () =>
        {
            try
            {
                await WriteMessageAsync(pipeStream, writeLock, new CancelMessage(), CancellationToken.None);
            }
            catch (Exception ex)
            {
                _traceLogger.Trace($"{ElevatedHelperTag} CancelMessage write threw {ex.GetType().Name}: {ex.Message} (likely helper already exited)");
            }
        });

        var graceCts = new CancellationTokenSource();
        killState.SetGraceTimer(graceCts);

        var killTask = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_cancellationGrace, graceCts.Token);

                _traceLogger.Warning($"{ElevatedHelperTag} Helper did not respond with a Result message within {_cancellationGrace.TotalSeconds:N0}s of CancelMessage - force-killing.");

                if (process.Kill())
                {
                    killState.MarkKillSucceeded();
                }
                else
                {
                    killState.MarkKillFailed();

                    _traceLogger.Error($"{ElevatedHelperTag} Kill returned false; helper may continue running as orphan. Disposing pipe to unblock drain loop.");

                    try { await ((IAsyncDisposable)process.Pipe).DisposeAsync(); }
                    catch { /* best effort */ }
                }
            }
            catch (OperationCanceledException) { /* helper finished in time */ }
            catch (Exception ex)
            {
                _traceLogger.Warning($"{ElevatedHelperTag} kill-timer task threw {ex.GetType().Name}: {ex.Message}");
            }
        });

        killState.SetKillTask(killTask);
    }

    private void MirrorMessageToDebugLog(DatabaseToolsIpcMessage message)
    {
        switch (message)
        {
            case HelloMessage h:
                _traceLogger.Trace($"{ElevatedHelperTag} Hello: helperPid={h.HelperProcessId}, protocol={h.ProtocolVersion}");
                break;

            case ResultMessage { Outcome: DatabaseToolsOutcome.Succeeded } r:
                _traceLogger.Trace($"{ElevatedHelperTag} Result: Succeeded ({r.DurationMs} ms).");
                break;

            case ResultMessage { Outcome: DatabaseToolsOutcome.Cancelled } r:
                _traceLogger.Information($"{ElevatedHelperTag} Result: Cancelled ({r.DurationMs} ms). {r.FailureSummary}");
                break;

            case ResultMessage r:
                _traceLogger.Error($"{ElevatedHelperTag} Result: Failed ({r.DurationMs} ms). {r.FailureSummary}");
                break;

            case FatalMessage f:
                _traceLogger.Error($"{ElevatedHelperTag} Fatal: {f.ExceptionType}: {f.Message}");

                if (!string.IsNullOrWhiteSpace(f.StackTrace))
                {
                    _traceLogger.Error($"{ElevatedHelperTag} Fatal stack: {f.StackTrace}");
                }

                break;

            case ProbeMessage p:
                _traceLogger.Warning($"{ElevatedHelperTag} (unexpected) Probe message received during operation path: processPath={p.ProcessPath}, integrity={p.IntegrityLevel}, packageIdentityOk={p.PackageIdentityOk}");
                break;

            case CancelMessage:
                _traceLogger.Warning($"{ElevatedHelperTag} (unexpected) CancelMessage received from helper. CancelMessage is a runner-to-helper control message; helpers must not emit it.");
                break;

            case ImageEditionsMessage e:
                _traceLogger.Trace($"{ElevatedHelperTag} ImageEditions: status={e.Status}, count={e.Images.Count}.");
                break;
        }
    }

    private async Task<DatabaseToolsResult> RunAsync(
        DatabaseToolsIpcRequest request,
        IProgress<LogRecord> logSink,
        IProgress<DatabaseToolsProgress>? progressSink,
        CancellationToken cancellationToken,
        Action<DatabaseToolsIpcMessage>? onDataMessage = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(logSink);

        var stopwatch = Stopwatch.StartNew();
        var killState = new KillState();
        IElevatedHelperProcess? process = null;
        SemaphoreSlim? writeLock = null;
        CancellationTokenRegistration cancelRegistration = default;
        Task? pipeReaderTask = null;
        CancellationTokenSource? readerStopCts = null;
        bool exitHandled = false;

        _traceLogger.Trace($"{ElevatedHelperTag} Starting {request.GetType().Name} (verbose={request.Verbose})...");

        try
        {
            try
            {
                process = await _host.StartAsync(Array.Empty<string>(), cancellationToken);
            }
            catch (Win32Exception w32) when (w32.NativeErrorCode == 1223)
            {
                _traceLogger.Information($"{ElevatedHelperTag} User declined the UAC prompt.");

                return new DatabaseToolsResult(DatabaseToolsOutcome.Cancelled, "User declined the UAC prompt.", stopwatch.Elapsed);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _traceLogger.Information($"{ElevatedHelperTag} Caller cancelled before helper spawn completed.");

                return new DatabaseToolsResult(DatabaseToolsOutcome.Cancelled, "Cancelled before helper spawn completed.", stopwatch.Elapsed);
            }
            catch (FileNotFoundException fnf)
            {
                _traceLogger.Error($"{ElevatedHelperTag} Helper executable not found: {fnf.Message}");

                return new DatabaseToolsResult(DatabaseToolsOutcome.Failed, $"Elevation helper not found: {fnf.Message}", stopwatch.Elapsed);
            }

            _traceLogger.Trace($"{ElevatedHelperTag} Helper process started; PID={process.ProcessId}.");

            writeLock = new SemaphoreSlim(initialCount: 1, maxCount: 1);

            var channel = Channel.CreateBounded<DatabaseToolsIpcMessage>(
                new BoundedChannelOptions(ChannelCapacity)
                {
                    SingleReader = true,
                    SingleWriter = true,
                    FullMode = BoundedChannelFullMode.Wait
                });

            readerStopCts = new CancellationTokenSource();

            var pipeStream = process.Pipe;
            pipeReaderTask = Task.Run(
                () => DrainPipeAsync(pipeStream, channel.Writer, readerStopCts.Token),
                readerStopCts.Token);

            try
            {
                using var helloCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                helloCts.CancelAfter(_helloTimeout);

                var first = await channel.Reader.ReadAsync(helloCts.Token);
                MirrorMessageToDebugLog(first);

                if (first is HelloMessage hello)
                {
                    if (hello.HelperProcessId != process.ProcessId)
                    {
                        _traceLogger.Warning($"{ElevatedHelperTag} Hello.HelperProcessId={hello.HelperProcessId} does not match spawned PID={process.ProcessId}; continuing (PID was already verified at pipe-accept time).");
                    }

                    if (hello.ProtocolVersion != HelloMessage.CurrentProtocolVersion)
                    {
                        return new DatabaseToolsResult(
                            DatabaseToolsOutcome.Failed,
                            $"Helper IPC protocol version mismatch: helper sent {hello.ProtocolVersion}, runner expected {HelloMessage.CurrentProtocolVersion}. The helper EXE may be from a different app version - reinstall the MSIX so the main app and helper ship together.",
                            stopwatch.Elapsed);
                    }
                }
                else
                {
                    return new DatabaseToolsResult(
                        DatabaseToolsOutcome.Failed,
                        $"Helper sent {first.GetType().Name} instead of HelloMessage as its first message.",
                        stopwatch.Elapsed);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new DatabaseToolsResult(DatabaseToolsOutcome.Cancelled, "Cancelled before helper handshake.", stopwatch.Elapsed);
            }
            catch (OperationCanceledException)
            {
                return new DatabaseToolsResult(
                    DatabaseToolsOutcome.Failed,
                    $"Helper did not send Hello message within {_helloTimeout.TotalSeconds:N0}s.",
                    stopwatch.Elapsed);
            }
            catch (ChannelClosedException)
            {
                return new DatabaseToolsResult(
                    DatabaseToolsOutcome.Failed,
                    "Pipe closed before helper sent Hello message (helper likely crashed during startup).",
                    stopwatch.Elapsed);
            }

            try
            {
                await WriteRequestAsync(pipeStream, writeLock, request, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return new DatabaseToolsResult(DatabaseToolsOutcome.Cancelled, "Cancelled while sending request to helper.", stopwatch.Elapsed);
            }

            if (cancellationToken.CanBeCanceled)
            {
                cancelRegistration = cancellationToken.Register(() => HandleCallerCancellation(pipeStream, writeLock, process, killState));
            }

            ResultMessage? result = null;
            FatalMessage? fatal = null;

            try
            {
                while (await channel.Reader.WaitToReadAsync(CancellationToken.None).ConfigureAwait(false))
                {
                    if (!channel.Reader.TryRead(out var message)) { continue; }

                    MirrorMessageToDebugLog(message);

                    switch (message)
                    {
                        case LogMessage log:
                            SafeReport(logSink, new LogRecord(log.TimestampUtc, log.Level, log.Message));
                            break;

                        case ProgressMessage prog when progressSink is not null:
                            SafeReport(progressSink, new DatabaseToolsProgress(prog.Processed, prog.Total, prog.CurrentItem));
                            break;

                        case ImageEditionsMessage edition when onDataMessage is not null:
                            try { onDataMessage(edition); }
                            catch (Exception ex)
                            {
                                _traceLogger.Warning($"{ElevatedHelperTag} onDataMessage callback threw {ex.GetType().Name}: {ex.Message}");
                            }

                            break;

                        case ResultMessage r:
                            result = r;
                            break;

                        case FatalMessage f:
                            fatal = f;
                            break;
                    }

                    if (result is not null || fatal is not null) { break; }
                }
            }
            finally
            {
                await cancelRegistration.DisposeAsync();
            }

            killState.CancelGraceTimer();

            // Join the kill-timer so its disposition write happens-before the TranslateOutcome read.
            try { await killState.KillTaskOrCompleted.WaitAsync(_exitGrace); }
            catch (TimeoutException)
            {
                _traceLogger.Warning($"{ElevatedHelperTag} kill-timer did not settle within {_exitGrace.TotalSeconds:N0}s; proceeding with current disposition.");
            }

            int exitCode;
            try
            {
                using var exitCts = new CancellationTokenSource(_exitGrace);

                exitCode = await process.WaitForExitAsync(exitCts.Token);
            }
            catch (OperationCanceledException)
            {
                if (killState.Disposition != KillDisposition.Failed)
                {
                    _traceLogger.Warning($"{ElevatedHelperTag} Helper did not exit within {_exitGrace.TotalSeconds:N0}s after IPC drained - force-killing.");

                    if (process.Kill())
                    {
                        killState.MarkKillSucceeded();
                    }
                    else
                    {
                        killState.MarkKillFailed();

                        _traceLogger.Error($"{ElevatedHelperTag} Kill returned false; helper may continue running as orphan. Disposing pipe.");

                        try { await ((IAsyncDisposable)process.Pipe).DisposeAsync(); }
                        catch { /* best effort */ }
                    }
                }

                if (killState.Disposition == KillDisposition.Failed)
                {
                    exitCode = -1;
                }
                else
                {
                    try
                    {
                        using var killWaitCts = new CancellationTokenSource(_exitGrace);

                        exitCode = await process.WaitForExitAsync(killWaitCts.Token);
                    }
                    catch { exitCode = -1; }
                }
            }

            readerStopCts.Cancel();

            try { await pipeReaderTask; }
            catch (OperationCanceledException) { /* normal */ }
            catch (Exception ex)
            {
                _traceLogger.Warning($"{ElevatedHelperTag} Pipe reader task ended with {ex.GetType().Name}: {ex.Message}");
            }

            _traceLogger.Trace($"{ElevatedHelperTag} Helper process exited; exit code = {exitCode}{(killState.HelperKilled ? " (force-killed)" : string.Empty)}.");

            exitHandled = true;

            return TranslateOutcome(result, fatal, exitCode, killState.Disposition, cancellationToken, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            _traceLogger.Error($"{ElevatedHelperTag} Unhandled exception in runner: {ex}");

            return new DatabaseToolsResult(DatabaseToolsOutcome.Failed, $"{ex.GetType().Name}: {ex.Message}", stopwatch.Elapsed);
        }
        finally
        {
            killState.CancelGraceTimer();

            if (!exitHandled && process is not null)
            {
                try { readerStopCts?.Cancel(); }
                catch (ObjectDisposedException) { /* already disposed */ }

                if (pipeReaderTask is not null)
                {
                    try { await pipeReaderTask.WaitAsync(_exitGrace); }
                    catch { /* best effort */ }
                }

                // Dispose pipe before force-kill so the helper can exit cooperatively.
                try { await ((IAsyncDisposable)process.Pipe).DisposeAsync(); }
                catch { /* best effort */ }

                try
                {
                    using var cleanupCts = new CancellationTokenSource(_exitGrace);

                    await process.WaitForExitAsync(cleanupCts.Token);
                }
                catch
                {
                    if (process.Kill())
                    {
                        killState.MarkKillSucceeded();

                        try
                        {
                            using var forceCts = new CancellationTokenSource(_exitGrace);

                            await process.WaitForExitAsync(forceCts.Token);
                        }
                        catch { /* best effort */ }
                    }
                    else
                    {
                        killState.MarkKillFailed();
                    }
                }
            }

            killState.DisposeGraceTimer();
            writeLock?.Dispose();
            readerStopCts?.Dispose();

            if (process is not null)
            {
                try { await process.DisposeAsync(); }
                catch { /* best effort */ }
            }
        }
    }

    private void SafeReport<T>(IProgress<T> sink, T value)
    {
        try
        {
            sink.Report(value);
        }
        catch (Exception ex)
        {
            _traceLogger.Warning($"{ElevatedHelperTag} {typeof(IProgress<T>).Name}.Report threw {ex.GetType().Name}: {ex.Message}");
        }
    }

    private DatabaseToolsResult TranslateOutcome(
        ResultMessage? result,
        FatalMessage? fatal,
        int exitCode,
        KillDisposition killDisposition,
        CancellationToken cancellationToken,
        TimeSpan elapsed)
    {
        if (result is not null)
        {
            return new DatabaseToolsResult(
                result.Outcome,
                result.FailureSummary,
                TimeSpan.FromMilliseconds(result.DurationMs));
        }

        if (fatal is not null)
        {
            return new DatabaseToolsResult(
                DatabaseToolsOutcome.Failed,
                $"Helper threw {fatal.ExceptionType}: {fatal.Message}",
                elapsed);
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            return new DatabaseToolsResult(
                DatabaseToolsOutcome.Failed,
                $"Helper exited (code {exitCode}) without sending a Result message.",
                elapsed);
        }

        string summary = killDisposition switch
        {
            KillDisposition.Failed =>
                "Cancelled (helper did not respond to cancel and could not be terminated; it may continue running as an orphan process). " +
                "If you ran an Upgrade, a .bak of the original target may remain next to it - rename it to recover.",
            KillDisposition.Succeeded =>
                "Cancelled (helper did not respond to CancelMessage within grace window; force-killed). " +
                "If you ran an Upgrade, a .bak of the original target may remain next to it - rename it to recover.",
            _ => "Cancelled."
        };

        return new DatabaseToolsResult(DatabaseToolsOutcome.Cancelled, summary, elapsed);
    }

    private sealed class KillState
    {
        private int _cancelRequested;
        private int _disposition;
        private CancellationTokenSource? _graceTimerCts;
        private Task? _killTask;

        public KillDisposition Disposition => (KillDisposition)Volatile.Read(ref _disposition);

        public bool HelperKilled => Disposition == KillDisposition.Succeeded;

        public Task KillTaskOrCompleted => Volatile.Read(ref _killTask) ?? Task.CompletedTask;

        public void CancelGraceTimer()
        {
            try { Volatile.Read(ref _graceTimerCts)?.Cancel(); }
            catch (ObjectDisposedException) { /* ok */ }
        }

        public void DisposeGraceTimer()
        {
            try { Volatile.Read(ref _graceTimerCts)?.Dispose(); }
            catch { /* best effort */ }
        }

        public bool MarkCancelRequested() => Interlocked.Exchange(ref _cancelRequested, 1) == 0;

        public void MarkKillFailed() => Interlocked.CompareExchange(
            ref _disposition,
            (int)KillDisposition.Failed,
            (int)KillDisposition.NotAttempted);

        public void MarkKillSucceeded() => Interlocked.Exchange(ref _disposition, (int)KillDisposition.Succeeded);

        public void SetGraceTimer(CancellationTokenSource cts) => Volatile.Write(ref _graceTimerCts, cts);

        public void SetKillTask(Task task) => Volatile.Write(ref _killTask, task);
    }
}
