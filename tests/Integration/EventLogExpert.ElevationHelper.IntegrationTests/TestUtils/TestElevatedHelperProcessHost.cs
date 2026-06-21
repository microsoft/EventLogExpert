// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.DatabaseTools.Elevation;
using Microsoft.Win32.SafeHandles;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace EventLogExpert.ElevationHelper.IntegrationTests.TestUtils;

internal sealed class TestElevatedHelperProcessHost(ITraceLogger logger) : IElevatedHelperProcessHost
{
    private const int MaxClientPidRejections = 32;
    private const int MaxStderrChars = 4096;
    private const int PipeBufferSize = 65536;

    private static readonly TimeSpan s_connectTimeout = TimeSpan.FromSeconds(60);

    internal enum PidVerifyResult
    {
        Match = 0,
        ClientPidMismatch = 1,
        GetPidFailed = 2
    }

    public async Task<IElevatedHelperProcess> StartAsync(IReadOnlyList<string> extraArgs, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(extraArgs);

        var helperDllPath = Path.Combine(AppContext.BaseDirectory, "eventlogexpert-elevated.dll");

        if (!File.Exists(helperDllPath))
        {
            throw new FileNotFoundException(
                $"Elevation helper assembly not found at {helperDllPath}. Build the integration test project (ProjectReference copies it to bin).",
                helperDllPath);
        }

        var pipeName = $"eventlogexpert-elevated-test-{Guid.NewGuid():N}";
        var runtimeConfigPath = ResolveTestRuntimeConfig();
        var arguments = BuildArgumentString(runtimeConfigPath, helperDllPath, pipeName, extraArgs);

        var pipeServer = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
            inBufferSize: PipeBufferSize,
            outBufferSize: PipeBufferSize);

        Process? helperProcess = null;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            helperProcess = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start returned null without throwing.");

            var capturedPid = helperProcess.Id;
            var stderrLock = new object();
            var stderrBuffer = new StringBuilder();

            helperProcess.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null) { return; }

                logger.Warning($"helper[{capturedPid}] stderr: {e.Data}");

                lock (stderrLock)
                {
                    var remaining = MaxStderrChars - stderrBuffer.Length;

                    if (remaining <= 0) { return; }

                    stderrBuffer.Append(e.Data.Length <= remaining ? e.Data : e.Data[..remaining]);

                    if (stderrBuffer.Length < MaxStderrChars) { stderrBuffer.Append('\n'); }
                }
            };
            helperProcess.OutputDataReceived += (_, e) => { if (e.Data is not null) { logger.Information($"helper[{capturedPid}] stdout: {e.Data}"); } };
            helperProcess.BeginErrorReadLine();
            helperProcess.BeginOutputReadLine();

            string SnapshotStderr() { lock (stderrLock) { return stderrBuffer.ToString(); } }

            logger.Information($"{nameof(TestElevatedHelperProcessHost)}: spawned helper PID {helperProcess.Id} (pipe={pipeName}, dotnet {helperDllPath})");

            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(s_connectTimeout);

            // Watch for early helper exit on its own source so a connect timeout cannot misreport it as an exit.
            using var exitWatchCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            var acceptTask = AcceptAndVerifyClientPidAsync(pipeServer, helperProcess.Id, logger, connectCts.Token);
            var exitTask = WaitForExitOrCancelAsync(helperProcess, exitWatchCts.Token);

            try
            {
                var winner = await Task.WhenAny(acceptTask, exitTask);

                if (winner == exitTask && exitTask.Result && !acceptTask.IsCompletedSuccessfully)
                {
                    throw new InvalidOperationException(
                        $"Elevated helper PID {helperProcess.Id} exited (exit code {helperProcess.ExitCode}) before connecting. stderr tail: {SnapshotStderr()}");
                }

                await acceptTask;
            }
            catch (OperationCanceledException) when (connectCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                if (helperProcess.HasExited)
                {
                    throw new InvalidOperationException(
                        $"Elevated helper PID {helperProcess.Id} exited (exit code {helperProcess.ExitCode}) before connecting. stderr tail: {SnapshotStderr()}");
                }

                throw new TimeoutException(
                    $"Elevated helper PID {helperProcess.Id} did not connect within {s_connectTimeout.TotalSeconds:N0}s (helper still running). stderr tail: {SnapshotStderr()}");
            }
            finally
            {
                exitWatchCts.Cancel();
                connectCts.Cancel();
                await ObserveQuietly(acceptTask);
                await ObserveQuietly(exitTask);
            }

            var handle = new TestElevatedHelperProcess(helperProcess, pipeServer, logger);
            helperProcess = null;

            return handle;
        }
        catch
        {
            try { pipeServer.Dispose(); } catch { /* swallowed during error cleanup */ }

            if (helperProcess is null) { throw; }

            try
            {
                if (!helperProcess.HasExited) { helperProcess.Kill(entireProcessTree: false); }
            }
            catch (InvalidOperationException) { /* already exited */ }
            catch (Exception ex)
            {
                logger.Error(
                    $"{nameof(TestElevatedHelperProcessHost)}: failed to kill spawned helper PID {helperProcess.Id} during error cleanup: {ex.GetType().Name}: {ex.Message}");
            }

            helperProcess.Dispose();

            throw;
        }
    }

    internal static async Task AcceptAndVerifyClientPidAsync(
        NamedPipeServerStream pipeServer,
        int expectedPid,
        ITraceLogger logger,
        CancellationToken cancellationToken)
    {
        int rejections = 0;

        while (true)
        {
            await pipeServer.WaitForConnectionAsync(cancellationToken);

            var result = TryVerifyClientPid(pipeServer, expectedPid);

            if (result == PidVerifyResult.Match) { return; }

            if (result == PidVerifyResult.GetPidFailed)
            {
                var error = Marshal.GetLastWin32Error();

                throw new InvalidOperationException($"GetNamedPipeClientProcessId failed (Win32 error {error}).");
            }

            pipeServer.Disconnect();
            rejections++;

            if (rejections >= MaxClientPidRejections)
            {
                throw new InvalidOperationException(
                    $"Rejected {MaxClientPidRejections} same-user pipe connections from non-helper PIDs before the legitimate helper connected.");
            }

            if (rejections == 1)
            {
                logger.Information(
                    $"{nameof(TestElevatedHelperProcessHost)}: rejected pipe connection from non-helper PID (expected {expectedPid}); continuing to wait for legitimate helper.");
            }
            else
            {
                logger.Trace($"{nameof(TestElevatedHelperProcessHost)}: rejected pipe connection #{rejections} from non-helper PID.");
            }
        }
    }

    internal static PidVerifyResult TryVerifyClientPid(NamedPipeServerStream pipeServer, int expectedPid)
    {
        if (!NativeMethods.GetNamedPipeClientProcessId(pipeServer.SafePipeHandle, out var clientPid))
        {
            return PidVerifyResult.GetPidFailed;
        }

        return clientPid == (uint)expectedPid ? PidVerifyResult.Match : PidVerifyResult.ClientPidMismatch;
    }

    private static string BuildArgumentString(string runtimeConfigPath, string helperDllPath, string pipeName, IReadOnlyList<string> extraArgs)
    {
        var parts = new List<string>(extraArgs.Count + 5)
        {
            "exec",
            "--runtimeconfig",
            runtimeConfigPath,
            helperDllPath,
            "--pipe",
            pipeName
        };
        parts.AddRange(extraArgs);

        return string.Join(' ', parts.Select(QuoteIfNeeded));
    }

    private static async Task ObserveQuietly(Task task)
    {
        try { await task; }
        catch { /* losing-race tasks throw on cleanup cancellation; observed and dropped */ }
    }

    private static string QuoteIfNeeded(string arg)
    {
        if (string.IsNullOrEmpty(arg)) { return "\"\""; }

        if (arg.IndexOfAny([' ', '\t', '"']) < 0) { return arg; }

        return "\"" + arg.Replace("\"", "\\\"") + "\"";
    }

    private static string ResolveTestRuntimeConfig()
    {
        var testRuntimeConfig = Path.Combine(
            AppContext.BaseDirectory,
            $"{typeof(TestElevatedHelperProcessHost).Assembly.GetName().Name}.runtimeconfig.json");

        if (!File.Exists(testRuntimeConfig))
        {
            throw new FileNotFoundException(
                $"Test runtimeconfig.json not found at {testRuntimeConfig}; cannot make helper framework-dependent.",
                testRuntimeConfig);
        }

        return testRuntimeConfig;
    }

    private static async Task<bool> WaitForExitOrCancelAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            await process.WaitForExitAsync(cancellationToken);

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint clientProcessId);
    }
}

internal sealed class TestElevatedHelperProcess(Process process, NamedPipeServerStream pipe, ITraceLogger logger) : IElevatedHelperProcess
{
    private int _disposed;

    public Stream Pipe => pipe;

    public int ProcessId { get; } = process.Id;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) { return; }

        try { await pipe.DisposeAsync(); } catch { /* swallowed during dispose */ }

        try { process.Dispose(); } catch { /* swallowed during dispose */ }
    }

    public bool Kill()
    {
        try
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: false); }

            return true;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
        catch (Exception ex)
        {
            logger.Error($"{nameof(TestElevatedHelperProcess)}.{nameof(Kill)} failed: {ex.GetType().Name}: {ex.Message}");

            return false;
        }
    }

    public Task<int> WaitForExitAsync(CancellationToken cancellationToken)
    {
        return WaitAsync();

        async Task<int> WaitAsync()
        {
            await process.WaitForExitAsync(cancellationToken);

            return process.ExitCode;
        }
    }
}
