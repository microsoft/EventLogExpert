// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Ipc;
using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.ElevationHelper.Diagnostics;
using EventLogExpert.ElevationHelper.Ipc;
using EventLogExpert.ElevationHelper.Operations;
using EventLogExpert.Eventing.OfflineImaging.Workspace;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

namespace EventLogExpert.ElevationHelper;

internal static class ProgramEntry
{
    private const int CancelWatchdogExitCode = 12;
    // High-IL helper must kill itself if native code ignores cooperative cancellation; the medium-IL host cannot.
    private static readonly TimeSpan s_selfTerminateWatchdog = TimeSpan.FromSeconds(8);

    // Startup orphan cleanup must not block the control reader from receiving cancellation.
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
            try { await WriteCrashLogAsync(ex, args); } catch { /* Crash logging is best-effort. */ }

            try { await Console.Error.WriteLineAsync($"eventlogexpert-elevated: fatal: {ex}"); } catch { /* stderr may be unavailable. */ }

            return 3;
        }
    }

    private static async Task<int> RunOperationModeAsync(IpcMessageReader reader, IpcMessageWriter writer)
    {
        var stopwatch = Stopwatch.StartNew();

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

        using var operationCts = new CancellationTokenSource();

        // Armed on CancelMessage and cancelled on completion so only an ignored cooperative cancel self-terminates.
        using var watchdogCts = new CancellationTokenSource();

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

                    if (message is null) { return; }

                    if (message is CancelMessage)
                    {
                        operationCts.Cancel();

                        // If the operation does not unwind, self-terminate so the host observes pipe EOF promptly.
                        try { await Task.Delay(s_selfTerminateWatchdog, watchdogCts.Token); }
                        catch (OperationCanceledException) { return; }

                        await SelfTerminateAfterUnresponsiveCancelAsync(writer, stopwatch.ElapsedMilliseconds);

                        return;
                    }

                }
            }
            catch
            {
                // Control reader exceptions are isolated; main-flow teardown owns cleanup.
            }
        });

        // Run orphan cleanup off the main flow so a wedged delete cannot block cancellation handling.
        var reconcileTask = Task.Run(() =>
        {
            try { OfflineMaintenance.ReconcileOrphans(logger: null); }
            catch { /* Startup reconciliation is best-effort. */ }
        });

        try { await reconcileTask.WaitAsync(s_startupReconcileTimeout, operationCts.Token); }
        catch (TimeoutException) { /* Slow orphan cleanup continues in the background. */ }
        catch (OperationCanceledException) { /* Cancel watchdog handles shutdown. */ }

        DatabaseToolsResult result;
        try
        {
            result = await OperationDispatcher.DispatchAsync(request, writer, operationCts.Token);
        }
        catch (Exception ex)
        {
            watchdogCts.Cancel();
            operationCts.Cancel();
            try { await controlReaderTask.WaitAsync(TimeSpan.FromSeconds(1)); } catch { /* Best-effort control-reader drain. */ }

            await TryWriteTerminalAsync(writer,
                new FatalMessage(ex.GetType().FullName ?? ex.GetType().Name, ex.Message, ex.StackTrace ?? string.Empty));

            return 11;
        }

        watchdogCts.Cancel();
        operationCts.Cancel();

        try { await controlReaderTask.WaitAsync(TimeSpan.FromSeconds(1)); } catch { /* Best-effort control-reader drain. */ }

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
            // Omit client-side CurrentUserOnly: elevated and medium-IL same-user tokens have different SIDs; server DACL plus PID verification authenticates.
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

    // Last-resort self-termination leaves orphaned mounts for next elevated-launch reconciliation.
    private static async Task SelfTerminateAfterUnresponsiveCancelAsync(IpcMessageWriter writer, long elapsedMs)
    {
        try
        {
            await TryWriteTerminalAsync(writer, new ResultMessage(
                DatabaseToolsOutcome.Cancelled,
                "Cancelled; the elevated helper self-terminated after a native operation ignored cancellation.",
                elapsedMs)).WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch { /* Pipe EOF on exit is the guaranteed host signal. */ }

        Environment.Exit(CancelWatchdogExitCode);
    }

    private static async Task TryWriteTerminalAsync(IpcMessageWriter writer, DatabaseToolsIpcMessage message)
    {
        try { await writer.WriteAsync(message, CancellationToken.None); }
        catch (IOException) { /* runner disconnected before terminal message was written; not a helper crash */ }
        catch (ObjectDisposedException) { /* pipe disposed by runner; not a helper crash */ }
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
            }
        }

        return new HelperArgs(pipeName, probe);
    }
}
