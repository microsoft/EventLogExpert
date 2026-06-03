// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Contracts;
using EventLogExpert.Logging.Abstractions;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace EventLogExpert.Runtime.DatabaseTools.Elevation;

/// <summary>
///     Production <see cref="IElevatedDatabaseToolsRunner" /> implementation. For each request: spawn helper → wait
///     for Hello envelope (10s) → send request → drain envelopes through a bounded <see cref="Channel{T}" /> → forward
///     Log/Progress to caller sinks + mirror to <see cref="ITraceLogger" /> with <c>[ElevatedHelper]</c> prefix → capture
///     terminal Result/Fatal → await helper exit (5s grace, then force-kill) → translate to
///     <see cref="DatabaseToolsResult" />.
/// </summary>
/// <remarks>
///     <para>
///         <b>Cancellation flow:</b> caller cancels → runner writes <see cref="CancelEnvelope" /> to the pipe
///         (best-effort) and starts a 30s grace timer. Helper observes the envelope, cancels its operation CTS, and emits
///         a <see cref="ResultEnvelope" /> with <see cref="DatabaseToolsOutcome.Cancelled" />. If the helper does NOT emit
///         a result within the grace window, the runner force-kills the process and returns a synthesized Cancelled
///         outcome.
///     </para>
///     <para>
///         <b>Concurrency invariants:</b>
///         <list type="bullet">
///             <item>
///                 ONE dedicated pipe-reader task per RunAsync invocation, deserializing line-by-line into a bounded
///                 <see cref="Channel{T}" /> (capacity 1024). Bounded channel + named-pipe OS buffer together provide
///                 back-pressure to the helper if the caller's <see cref="IProgress{T}" /> sink is slow.
///             </item>
///             <item>Pipe WRITES (request + cancel) are guarded by a <see cref="SemaphoreSlim" />.</item>
///             <item>
///                 Concurrent read (drain task) + write (request + cancel) on the same <see cref="Stream" /> is safe
///                 ONLY because the underlying duplex named pipe has independent OS-level buffers per direction (see
///                 <see cref="IElevatedHelperProcess.Pipe" />'s contract documentation).
///             </item>
///             <item>
///                 <see cref="ITraceLogger" /> mirroring runs on the dispatcher (single thread); the underlying
///                 <see cref="EventLogExpert.Runtime.DebugLog.DebugLogService" /> uses Mutex + Lock + drop-trace fallback
///                 so concurrent appends from arbitrary callers are safe.
///             </item>
///         </list>
///     </para>
/// </remarks>
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

    public Task<DatabaseToolsResult> CreateAsync(CreateDatabaseRequest request, IProgress<DatabaseToolsLogEntry> logSink, IProgress<DatabaseToolsProgress>? progress, CancellationToken cancellationToken, bool verbose = false)
        => RunAsync(new CreateDatabaseIpcRequest(request, verbose), logSink, progress, cancellationToken);

    public Task<DatabaseToolsResult> DiffAsync(DiffDatabaseRequest request, IProgress<DatabaseToolsLogEntry> logSink, IProgress<DatabaseToolsProgress>? progress, CancellationToken cancellationToken, bool verbose = false)
        => RunAsync(new DiffDatabaseIpcRequest(request, verbose), logSink, progress, cancellationToken);

    public Task<DatabaseToolsResult> MergeAsync(MergeDatabaseRequest request, IProgress<DatabaseToolsLogEntry> logSink, IProgress<DatabaseToolsProgress>? progress, CancellationToken cancellationToken, bool verbose = false)
        => RunAsync(new MergeDatabaseIpcRequest(request, verbose), logSink, progress, cancellationToken);

    public Task<DatabaseToolsResult> ShowAsync(ShowProvidersRequest request, IProgress<DatabaseToolsLogEntry> logSink, IProgress<DatabaseToolsProgress>? progress, CancellationToken cancellationToken, bool verbose = false)
        => RunAsync(new ShowProvidersIpcRequest(request, verbose), logSink, progress, cancellationToken);

    public Task<DatabaseToolsResult> UpgradeAsync(UpgradeDatabaseRequest request, IProgress<DatabaseToolsLogEntry> logSink, IProgress<DatabaseToolsProgress>? progress, CancellationToken cancellationToken, bool verbose = false)
        => RunAsync(new UpgradeDatabaseIpcRequest(request, verbose), logSink, progress, cancellationToken);

    private static async Task DrainPipeAsync(Stream pipe, ChannelWriter<DatabaseToolsIpcEnvelope> writer, CancellationToken cancellationToken)
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

                DatabaseToolsIpcEnvelope envelope;

                try
                {
                    envelope = JsonSerializer.Deserialize<DatabaseToolsIpcEnvelope>(line, DatabaseToolsIpcSerializer.Options)
                        ?? throw new JsonException("Deserialized envelope was null.");
                }
                catch (Exception ex)
                {
                    envelope = new FatalEnvelope(
                        ex.GetType().FullName ?? ex.GetType().Name,
                        $"Malformed envelope from helper: {ex.Message} (line: {Truncate(line, 200)})",
                        ex.StackTrace ?? string.Empty);
                }

                try
                {
                    await writer.WriteAsync(envelope, cancellationToken);
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
        s.Length <= max ? s : s[..max] + "…";

    private static async Task WriteEnvelopeAsync(Stream pipe, SemaphoreSlim writeLock, DatabaseToolsIpcEnvelope envelope, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(envelope, DatabaseToolsIpcSerializer.Options);

        await WriteJsonLineAsync(pipe, writeLock, json, cancellationToken);
    }

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

    private static async Task WriteRequestAsync(Stream pipe, SemaphoreSlim writeLock, DatabaseToolsIpcRequest request, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(request, DatabaseToolsIpcSerializer.Options);

        await WriteJsonLineAsync(pipe, writeLock, json, cancellationToken);
    }

    private void HandleCallerCancellation(Stream pipeStream, SemaphoreSlim writeLock, IElevatedHelperProcess process, KillState killState)
    {
        if (!killState.MarkCancelRequested()) { return; }

        _traceLogger.Information($"{ElevatedHelperTag} Caller cancellation requested; sending CancelEnvelope to helper and starting {_cancellationGrace.TotalSeconds:N0}s grace window.");

        _ = Task.Run(async () =>
        {
            try
            {
                await WriteEnvelopeAsync(pipeStream, writeLock, new CancelEnvelope(), CancellationToken.None);
            }
            catch (Exception ex)
            {
                _traceLogger.Trace($"{ElevatedHelperTag} CancelEnvelope write threw {ex.GetType().Name}: {ex.Message} (likely helper already exited)");
            }
        });

        var graceCts = new CancellationTokenSource();
        killState.SetGraceTimer(graceCts);

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_cancellationGrace, graceCts.Token);

                _traceLogger.Warning($"{ElevatedHelperTag} Helper did not respond with a Result envelope within {_cancellationGrace.TotalSeconds:N0}s of CancelEnvelope — force-killing.");
                process.Kill();
                killState.MarkKilled();
            }
            catch (OperationCanceledException) { /* helper finished in time */ }
            catch (Exception ex)
            {
                _traceLogger.Warning($"{ElevatedHelperTag} kill-timer task threw {ex.GetType().Name}: {ex.Message}");
            }
        });
    }

    private void MirrorEnvelopeToDebugLog(DatabaseToolsIpcEnvelope envelope)
    {
        switch (envelope)
        {
            case HelloEnvelope h:
                _traceLogger.Trace($"{ElevatedHelperTag} Hello: helperPid={h.HelperProcessId}, protocol={h.ProtocolVersion}");
                break;

            case ResultEnvelope { Outcome: DatabaseToolsOutcome.Succeeded } r:
                _traceLogger.Trace($"{ElevatedHelperTag} Result: Succeeded ({r.DurationMs} ms).");
                break;

            case ResultEnvelope { Outcome: DatabaseToolsOutcome.Cancelled } r:
                _traceLogger.Information($"{ElevatedHelperTag} Result: Cancelled ({r.DurationMs} ms). {r.FailureSummary}");
                break;

            case ResultEnvelope r:
                _traceLogger.Error($"{ElevatedHelperTag} Result: Failed ({r.DurationMs} ms). {r.FailureSummary}");
                break;

            case FatalEnvelope f:
                _traceLogger.Error($"{ElevatedHelperTag} Fatal: {f.ExceptionType}: {f.Message}");

                if (!string.IsNullOrWhiteSpace(f.StackTrace))
                {
                    _traceLogger.Error($"{ElevatedHelperTag} Fatal stack: {f.StackTrace}");
                }
                break;

            case ProbeEnvelope p:
                _traceLogger.Warning($"{ElevatedHelperTag} (unexpected) Probe envelope received during operation path: processPath={p.ProcessPath}, integrity={p.IntegrityLevel}, packageIdentityOk={p.PackageIdentityOk}");
                break;

            case CancelEnvelope:
                _traceLogger.Warning($"{ElevatedHelperTag} (unexpected) CancelEnvelope received from helper. CancelEnvelope is a runner-to-helper control message; helpers must not emit it.");
                break;

            // LogEnvelope and ProgressEnvelope: intentionally not mirrored — operation output flows only to
            // the caller's IProgress<> sinks (modal log view), matching the non-elevated path.
        }
    }

    private async Task<DatabaseToolsResult> RunAsync(
        DatabaseToolsIpcRequest request,
        IProgress<DatabaseToolsLogEntry> logSink,
        IProgress<DatabaseToolsProgress>? progressSink,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(logSink);

        var stopwatch = Stopwatch.StartNew();
        var killState = new KillState();
        IElevatedHelperProcess? process = null;
        SemaphoreSlim? writeLock = null;
        CancellationTokenRegistration cancelRegistration = default;

        _traceLogger.Trace($"{ElevatedHelperTag} Starting {request.GetType().Name} (verbose={request.Verbose})…");

        try
        {
            // 1) Spawn helper.
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

            // 2) Set up the pipe-reader → bounded channel pipeline.
            var channel = Channel.CreateBounded<DatabaseToolsIpcEnvelope>(
                new BoundedChannelOptions(ChannelCapacity)
                {
                    SingleReader = true,
                    SingleWriter = true,
                    FullMode = BoundedChannelFullMode.Wait
                });

            using var readerStopCts = new CancellationTokenSource();

            var pipeStream = process.Pipe;
            var pipeReaderTask = Task.Run(
                () => DrainPipeAsync(pipeStream, channel.Writer, readerStopCts.Token),
                readerStopCts.Token);

            // 3) Wait for Hello envelope (deadline).
            try
            {
                using var helloCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                helloCts.CancelAfter(_helloTimeout);

                var first = await channel.Reader.ReadAsync(helloCts.Token);
                MirrorEnvelopeToDebugLog(first);

                if (first is HelloEnvelope hello)
                {
                    if (hello.HelperProcessId != process.ProcessId)
                    {
                        _traceLogger.Warning($"{ElevatedHelperTag} Hello.HelperProcessId={hello.HelperProcessId} does not match spawned PID={process.ProcessId}; continuing (PID was already verified at pipe-accept time).");
                    }

                    if (hello.ProtocolVersion != HelloEnvelope.CurrentProtocolVersion)
                    {
                        return new DatabaseToolsResult(
                            DatabaseToolsOutcome.Failed,
                            $"Helper IPC protocol version mismatch: helper sent {hello.ProtocolVersion}, runner expected {HelloEnvelope.CurrentProtocolVersion}. The helper EXE may be from a different app version — reinstall the MSIX so the main app and helper ship together.",
                            stopwatch.Elapsed);
                    }
                }
                else
                {
                    return new DatabaseToolsResult(
                        DatabaseToolsOutcome.Failed,
                        $"Helper sent {first.GetType().Name} instead of HelloEnvelope as its first envelope.",
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
                    $"Helper did not send Hello envelope within {_helloTimeout.TotalSeconds:N0}s.",
                    stopwatch.Elapsed);
            }
            catch (ChannelClosedException)
            {
                return new DatabaseToolsResult(
                    DatabaseToolsOutcome.Failed,
                    "Pipe closed before helper sent Hello envelope (helper likely crashed during startup).",
                    stopwatch.Elapsed);
            }

            // 4) Send the request.
            try
            {
                await WriteRequestAsync(pipeStream, writeLock, request, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return new DatabaseToolsResult(DatabaseToolsOutcome.Cancelled, "Cancelled while sending request to helper.", stopwatch.Elapsed);
            }

            // 5) Wire caller-cancellation → CancelEnvelope + grace timer.
            if (cancellationToken.CanBeCanceled)
            {
                cancelRegistration = cancellationToken.Register(() => HandleCallerCancellation(pipeStream, writeLock, process, killState));
            }

            // 6) Drain envelopes until Result or Fatal arrives (or pipe closes).
            ResultEnvelope? result = null;
            FatalEnvelope? fatal = null;

            try
            {
                while (await channel.Reader.WaitToReadAsync(CancellationToken.None))
                {
                    if (!channel.Reader.TryRead(out var envelope)) { continue; }

                    MirrorEnvelopeToDebugLog(envelope);

                    switch (envelope)
                    {
                        case LogEnvelope log:
                            SafeReport(logSink, new DatabaseToolsLogEntry(log.TimestampUtc, log.Level, log.Message));
                            break;

                        case ProgressEnvelope prog when progressSink is not null:
                            SafeReport(progressSink, new DatabaseToolsProgress(prog.Processed, prog.Total, prog.CurrentItem));
                            break;

                        case ResultEnvelope r:
                            result = r;
                            break;

                        case FatalEnvelope f:
                            fatal = f;
                            break;
                    }

                    if (result is not null || fatal is not null) { break; }
                }
            }
            finally
            {
                cancelRegistration.Dispose();
            }

            // Helper sent a terminal envelope (or pipe closed). Cancel the kill-timer so it doesn't fire on a clean finish.
            killState.CancelGraceTimer();

            // 7) Wait for helper to exit (grace, then force-kill).
            int exitCode;
            try
            {
                using var exitCts = new CancellationTokenSource();
                exitCts.CancelAfter(_exitGrace);

                exitCode = await process.WaitForExitAsync(exitCts.Token);
            }
            catch (OperationCanceledException)
            {
                if (!killState.HelperKilled)
                {
                    _traceLogger.Warning($"{ElevatedHelperTag} Helper did not exit within {_exitGrace.TotalSeconds:N0}s after IPC drained — force-killing.");
                    process.Kill();
                    killState.MarkKilled();
                }

                try { exitCode = await process.WaitForExitAsync(CancellationToken.None); }
                catch { exitCode = -1; }
            }

            // 8) Stop the pipe reader so it releases the StreamReader / Stream.
            readerStopCts.Cancel();

            try { await pipeReaderTask; }
            catch (OperationCanceledException) { /* normal */ }
            catch (Exception ex)
            {
                _traceLogger.Warning($"{ElevatedHelperTag} Pipe reader task ended with {ex.GetType().Name}: {ex.Message}");
            }

            _traceLogger.Trace($"{ElevatedHelperTag} Helper process exited; exit code = {exitCode}{(killState.HelperKilled ? " (force-killed)" : string.Empty)}.");

            // 9) Translate to DatabaseToolsResult.
            return TranslateOutcome(result, fatal, exitCode, killState.HelperKilled, cancellationToken, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            _traceLogger.Error($"{ElevatedHelperTag} Unhandled exception in runner: {ex}");

            if (process is not null && !killState.HelperKilled)
            {
                try { process.Kill(); } catch { /* best effort */ }
            }

            return new DatabaseToolsResult(DatabaseToolsOutcome.Failed, $"{ex.GetType().Name}: {ex.Message}", stopwatch.Elapsed);
        }
        finally
        {
            killState.DisposeGraceTimer();
            writeLock?.Dispose();

            if (process is not null)
            {
                await process.DisposeAsync();
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
        ResultEnvelope? result,
        FatalEnvelope? fatal,
        int exitCode,
        bool helperKilled,
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

        if (cancellationToken.IsCancellationRequested)
        {
            return new DatabaseToolsResult(
                DatabaseToolsOutcome.Cancelled,
                helperKilled
                    ? "Cancelled (helper did not respond to CancelEnvelope within grace window; force-killed). If you ran an Upgrade, a .bak of the original target may remain next to it — rename it to recover."
                    : "Cancelled.",
                elapsed);
        }

        return new DatabaseToolsResult(
            DatabaseToolsOutcome.Failed,
            $"Helper exited (code {exitCode}) without sending a Result envelope.",
            elapsed);
    }

    private sealed class KillState
    {
        private int _cancelRequested;
        private CancellationTokenSource? _graceTimerCts;
        private int _helperKilled;

        public bool HelperKilled => Volatile.Read(ref _helperKilled) != 0;

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

        public void MarkKilled() => Interlocked.Exchange(ref _helperKilled, 1);

        public void SetGraceTimer(CancellationTokenSource cts) => Volatile.Write(ref _graceTimerCts, cts);
    }
}
