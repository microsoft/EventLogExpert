// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Ipc;
using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.ElevationHelper.Diagnostics;
using EventLogExpert.ElevationHelper.Ipc;
using EventLogExpert.ElevationHelper.Operations;
using EventLogExpert.Eventing.PublisherMetadata.Offline;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

namespace EventLogExpert.ElevationHelper;

/// <summary>
///     Entry point of the elevation helper. Two production-relevant invocation modes:
///     <list type="bullet">
///         <item>
///             <c>--pipe &lt;name&gt; --probe</c> - connect to the host's duplex named pipe, emit a Hello, then a Probe
///             (environment diagnostics: process path, integrity level, package-identity status), then a Succeeded Result.
///             Exits 0. Manual diagnostic facility for verifying that a packaged helper still launches correctly after a
///             deploy (no in-tree caller, but invokable via direct <c>ShellExecute("runas")</c> + a one-off named-pipe
///             listener to re-validate the elevation path post-update).
///         </item>
///         <item>
///             <c>--pipe &lt;name&gt;</c> (no <c>--probe</c>) - operation mode. Reads a
///             <see cref="DatabaseToolsIpcRequest" /> from the pipe, dispatches via <see cref="OperationDispatcher" /> +
///             <see cref="DestructiveRecovery" />, streams Log/Progress messages during execution, emits a terminal Result
///             message, exits 0.
///         </item>
///     </list>
/// </summary>
/// <remarks>
///     When launched via <c>ShellExecute</c>/<c>runas</c> the helper has no console attached; stderr is unavailable
///     for diagnostics. To preserve troubleshooting visibility, the outer try/catch in <see cref="MainAsync" /> writes a
///     one-shot crash log to <c>%TEMP%\eventlogexpert-elevated-crash-&lt;pid&gt;-&lt;ts&gt;.log</c> on ANY uncaught
///     exception (only fires when something is actually wrong; no per-step trace noise in TEMP on successful runs).
/// </remarks>
internal static class ProgramEntry
{
    // Distinct exit code so a watchdog self-terminate is identifiable in logs (the runner ignores it for a cancel).
    private const int CancelWatchdogExitCode = 12;
    // After a CancelMessage the helper waits this long for the operation to unwind cooperatively before self-terminating.
    // Comfortably inside the runner's 30s cancellation grace so the helper releases its pipe first and the runner never
    // needs its (futile, from medium IL) force-kill. A native call wedged on denied writes never pumps the WIM abort
    // callback, so without this the operation would hang forever and the elevated child cannot be killed by the host.
    private static readonly TimeSpan s_selfTerminateWatchdog = TimeSpan.FromSeconds(8);

    // Upper bound the main flow waits for best-effort startup reconciliation before dispatching. A wedged native
    // RegUnLoadKey on an orphaned hive must never block startup before the control reader can process a CancelMessage and
    // self-terminate; past this bound the reconcile keeps running in the background, it just stops gating the operation.
    private static readonly TimeSpan s_startupReconcileTimeout = TimeSpan.FromSeconds(30);

    public static async Task<int> MainAsync(string[] args)
    {
        try
        {
            var parsed = HelperArgs.Parse(args);

            if (parsed.PipeName is { } pipeName)
            {
                return await RunPipeModeAsync(pipeName, parsed.Probe);
            }

            await Console.Error.WriteLineAsync("eventlogexpert-elevated: requires --pipe <name> [--probe]");

            return 2;
        }
        catch (Exception ex)
        {
            try { await WriteCrashLogAsync(ex, args); } catch { /* nothing more we can do */ }

            try { await Console.Error.WriteLineAsync($"eventlogexpert-elevated: fatal: {ex}"); } catch { /* stderr unavailable */ }

            return 3;
        }
    }

    private static async Task<int> RunOperationModeAsync(IpcMessageReader reader, IpcMessageWriter writer)
    {
        var stopwatch = Stopwatch.StartNew();

        // 1) Read request (with deadline). The control reader cannot start until we've consumed it
        //    because they share the same StreamReader.
        DatabaseToolsIpcRequest? request;

        try
        {
            using var requestCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            request = await reader.ReadRequestAsync(requestCts.Token);
        }
        catch (OperationCanceledException)
        {
            await TryWriteTerminalAsync(writer, new FatalMessage("System.TimeoutException", "Runner did not send request within 10s.", string.Empty));

            return 8;
        }
        catch (Exception ex)
        {
            await TryWriteTerminalAsync(writer, new FatalMessage(ex.GetType().FullName ?? ex.GetType().Name, $"Request read failed: {ex.Message}", ex.StackTrace ?? string.Empty));

            return 9;
        }

        if (request is null)
        {
            await TryWriteTerminalAsync(writer, new FatalMessage("System.IO.EndOfStreamException", "Pipe closed before runner sent request.", string.Empty));

            return 10;
        }

        // 2) Set up operation cancellation. The control reader cancels this on receipt of a CancelMessage.
        using var operationCts = new CancellationTokenSource();

        // Armed when a CancelMessage arrives and cancelled once the operation completes, so the self-terminate watchdog
        // fires ONLY for an operation that ignored the cooperative cancel (e.g. a native WIMApplyImage wedged on
        // Controlled Folder Access-denied writes, which never pumps the abort callback).
        using var watchdogCts = new CancellationTokenSource();

        // 3) Start the control reader. Loops on the same StreamReader until either (a) it sees a CancelMessage
        //    (cancels operationCts and returns), (b) the pipe closes (returns), or (c) the operation completes
        //    and the main flow disposes the reader.
        var controlReaderTask = Task.Run(async () =>
        {
            try
            {
                while (!operationCts.IsCancellationRequested)
                {
                    DatabaseToolsIpcMessage? message;
                    try
                    {
                        message = await reader.ReadMessageAsync(operationCts.Token);
                    }
                    catch (OperationCanceledException) { return; }
                    catch (IOException) { return; }
                    catch (ObjectDisposedException) { return; }

                    if (message is null) { return; } // EOF

                    if (message is CancelMessage)
                    {
                        operationCts.Cancel();

                        // Give the operation a brief window to unwind cooperatively. If it does not (a native call
                        // ignoring the abort callback), self-terminate: the host's pipe drain then sees EOF and returns
                        // promptly. The elevated helper CAN exit itself; the medium-IL host provably cannot kill it.
                        try { await Task.Delay(s_selfTerminateWatchdog, watchdogCts.Token); }
                        catch (OperationCanceledException) { return; }

                        await SelfTerminateAfterUnresponsiveCancelAsync(writer, stopwatch.ElapsedMilliseconds);

                        return;
                    }

                    // Other messages are unexpected on the runner-to-helper direction during operation; ignore.
                }
            }
            catch
            {
                // Control reader should never propagate exceptions; an unexpected throw means the pipe is dead
                // or the reader was disposed mid-read. Either way the operation continues and the main flow's
                // teardown handles cleanup.
            }
        });

        // 3a) Reclaim scratch resources (orphaned hive mounts / WIM extraction folders) a crashed or self-terminated
        //     prior run left behind. MUST run elevated (the medium-IL host cannot unload an HKLM hive). Deliberately
        //     started AFTER the control reader/watchdog are armed and run OFF the main flow: a wedged native RegUnLoadKey
        //     on a dead orphan must not block startup before a CancelMessage can be processed (that would reintroduce the
        //     unkillable hang this helper exists to prevent). Best-effort and self-swallowing; the main flow waits only up
        //     to s_startupReconcileTimeout, then dispatches while any slow reconcile finishes in the background.
        var reconcileTask = Task.Run(() =>
        {
            try { OfflineMaintenance.ReconcileOrphans(logger: null); }
            catch { /* startup reconciliation is best-effort */ }
        });

        try { await reconcileTask.WaitAsync(s_startupReconcileTimeout, operationCts.Token); }
        catch (TimeoutException) { /* a wedged unload keeps running in the background; proceed so the operation can run */ }
        catch (OperationCanceledException) { /* cancel arrived mid-reconcile; the armed watchdog handles self-terminate */ }

        // 4) Dispatch the operation.
        DatabaseToolsResult result;
        try
        {
            result = await OperationDispatcher.DispatchAsync(request, writer, operationCts.Token);
        }
        catch (Exception ex)
        {
            watchdogCts.Cancel();
            operationCts.Cancel();
            try { await controlReaderTask.WaitAsync(TimeSpan.FromSeconds(1)); } catch { /* swallow */ }

            await TryWriteTerminalAsync(writer,
                new FatalMessage(ex.GetType().FullName ?? ex.GetType().Name, ex.Message, ex.StackTrace ?? string.Empty));

            return 11;
        }

        // 5) Stop the control reader + self-terminate watchdog. The operation completed, so cancel the watchdog before
        //    it can fire, then cancel operationCts (makes the inner ReadMessageAsync throw) so the control loop returns.
        watchdogCts.Cancel();
        operationCts.Cancel();

        try { await controlReaderTask.WaitAsync(TimeSpan.FromSeconds(1)); } catch { /* swallow */ }

        // 6) Emit the terminal Result message.
        await TryWriteTerminalAsync(writer,
            new ResultMessage(result.Outcome, result.FailureSummary, (long)result.Duration.TotalMilliseconds));

        return 0;
    }

    private static async Task<int> RunPipeModeAsync(string pipeName, bool probe)
    {
        await using var pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            // PipeOptions.CurrentUserOnly is INTENTIONALLY OMITTED on the client side. It triggers
            // NamedPipeClientStream.ValidateRemotePipeUser() which compares the server's pipe owner SID against
            // the client's current-user SID. For an elevated client connecting to a medium-IL server (same user,
            // different token), the comparison fails with UnauthorizedAccessException - the limited-token SID
            // attached to the server pipe is not bit-identical to the full-token SID the elevated client uses.
            // Authentication is still safe: the SERVER pipe has CurrentUserOnly (DACL restricted to same user)
            // AND the server PID-verifies the connected client via GetNamedPipeClientProcessId.
            PipeOptions.Asynchronous);

        try
        {
            await pipe.ConnectAsync(timeout: 10_000);
        }
        catch (TimeoutException)
        {
            await Console.Error.WriteLineAsync($"eventlogexpert-elevated: failed to connect to pipe '{pipeName}' within 10s.");

            return 4;
        }

        await using var writer = new IpcMessageWriter(pipe);
        using var reader = new IpcMessageReader(pipe);

        await writer.WriteAsync(new HelloMessage(Environment.ProcessId, HelloMessage.CurrentProtocolVersion), CancellationToken.None);

        if (!probe)
        {
            return await RunOperationModeAsync(reader, writer);
        }

        try
        {
            var probeMessage = ProbeMode.Capture();
            await writer.WriteAsync(probeMessage, CancellationToken.None);
            await writer.WriteAsync(new ResultMessage(DatabaseToolsOutcome.Succeeded, FailureSummary: null, DurationMs: 0), CancellationToken.None);

            return 0;
        }
        catch (Exception ex)
        {
            await writer.WriteAsync(
                new FatalMessage(ex.GetType().FullName ?? ex.GetType().Name, ex.Message, ex.StackTrace ?? string.Empty),
                CancellationToken.None);

            return 5;
        }
    }

    // Last-resort cancellation for an operation that ignored the cooperative cancel (a native call wedged on denied
    // writes never pumps the WIM abort callback). The elevated helper terminates ITSELF - the medium-IL host cannot kill
    // a high-IL child. Any orphaned hive mount / WIM extraction is reclaimed by the next elevated launch's startup
    // reconciliation, because their machine-global ownership beacons auto-release on process death.
    private static async Task SelfTerminateAfterUnresponsiveCancelAsync(IpcMessageWriter writer, long elapsedMs)
    {
        // Best-effort terminal Result so the host shows a clean Cancelled outcome with a useful summary instead of only
        // inferring EOF. The operation thread is wedged in native code, so the writer is not concurrently in use; bound
        // the write so a blocked pipe cannot itself re-wedge the exit.
        try
        {
            await TryWriteTerminalAsync(writer, new ResultMessage(
                DatabaseToolsOutcome.Cancelled,
                "Cancelled; the elevated helper self-terminated after a native operation ignored cancellation.",
                elapsedMs)).WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch { /* best-effort; the pipe EOF on exit is the guaranteed signal to the host */ }

        Environment.Exit(CancelWatchdogExitCode);
    }

    private static async Task TryWriteTerminalAsync(IpcMessageWriter writer, DatabaseToolsIpcMessage message)
    {
        try { await writer.WriteAsync(message, CancellationToken.None); }
        catch (IOException) { /* runner disconnected before terminal message was written; not a helper crash */ }
        catch (ObjectDisposedException) { /* pipe disposed by runner; same as above */ }
    }

    private static async Task WriteCrashLogAsync(Exception ex, string[] args)
    {
        var tempDir = Environment.GetEnvironmentVariable("TEMP")
            ?? Environment.GetEnvironmentVariable("TMP")
            ?? Path.GetTempPath();

        var path = Path.Combine(
            tempDir,
            $"eventlogexpert-elevated-crash-{Environment.ProcessId}-{DateTime.UtcNow:yyyyMMddTHHmmssfff}.log");

        var content = new StringBuilder();
        content.AppendLine("EventLogExpert elevation helper - crash log");
        content.AppendLine("============================================");
        content.AppendLine($"Timestamp UTC : {DateTime.UtcNow:O}");
        content.AppendLine($"Process Id    : {Environment.ProcessId}");
        content.AppendLine($"Process Path  : {Environment.ProcessPath ?? "(null)"}");
        content.AppendLine($"Working Dir   : {Environment.CurrentDirectory}");
        content.AppendLine($"User          : {Environment.UserDomainName}\\{Environment.UserName}");
        content.AppendLine($"OS Version    : {Environment.OSVersion}");
        content.AppendLine($"CLR Version   : {Environment.Version}");
        content.AppendLine($"CmdLine Args  : [{string.Join(", ", args.Select(a => $"'{a}'"))}]");
        content.AppendLine();
        content.AppendLine("--- Exception ---");
        content.AppendLine(ex.ToString());

        await File.WriteAllTextAsync(path, content.ToString(), Encoding.UTF8);
    }
}

/// <summary>Parsed CLI args. Tolerant: unknown args are ignored so future flags don't break older builds.</summary>
internal sealed record HelperArgs(string? PipeName, bool Probe)
{
    public static HelperArgs Parse(string[] args)
    {
        string? pipeName = null;
        bool probe = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pipe":
                    if (i + 1 < args.Length) { pipeName = args[++i]; }

                    break;
                case "--probe":
                    probe = true;

                    break;
                    // Unknown args: ignore.
            }
        }

        return new HelperArgs(pipeName, probe);
    }
}
