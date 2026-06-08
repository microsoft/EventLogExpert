// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Ipc;
using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.ElevationHelper.Diagnostics;
using EventLogExpert.ElevationHelper.Ipc;
using EventLogExpert.ElevationHelper.Operations;
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

        // 4) Dispatch the operation.
        DatabaseToolsResult result;
        try
        {
            result = await OperationDispatcher.DispatchAsync(request, writer, operationCts.Token);
        }
        catch (Exception ex)
        {
            operationCts.Cancel();
            try { await controlReaderTask.WaitAsync(TimeSpan.FromSeconds(1)); } catch { /* swallow */ }

            await TryWriteTerminalAsync(writer,
                new FatalMessage(ex.GetType().FullName ?? ex.GetType().Name, ex.Message, ex.StackTrace ?? string.Empty));

            return 11;
        }

        // 5) Stop the control reader. operationCts.Cancel() makes the inner ReadMessageAsync throw
        //    OperationCanceledException; the catch in the loop returns.
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
