// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.DatabaseTools.Elevation;
using Microsoft.Win32.SafeHandles;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace EventLogExpert.WindowsPlatform.Elevation;

/// <summary>
///     Windows implementation of <see cref="IElevatedHelperProcessHost" />. Spawns the packaged
///     <c>eventlogexpert-elevated.exe</c> via <c>ShellExecute</c> with verb <c>runas</c> (the only Win32 path that
///     triggers UAC), then accepts the helper's connect-back on a host-owned <see cref="NamedPipeServerStream" />. PID
///     verification uses <c>GetNamedPipeClientProcessId</c>: any client whose PID does not match the spawned helper is
///     rejected. The pipe ACL itself is restricted to the current user via <see cref="PipeOptions.CurrentUserOnly" />.
/// </summary>
internal sealed class ElevatedHelperProcessHost(ITraceLogger logger) : IElevatedHelperProcessHost
{
    private const int MaxClientPidRejections = 32;
    private const int PipeBufferSize = 65536;

    private static readonly TimeSpan s_pipeConnectTimeout = TimeSpan.FromSeconds(30);

    internal enum PidVerifyResult
    {
        Match = 0,
        ClientPidMismatch = 1,
        GetPidFailed = 2
    }

    public async Task<IElevatedHelperProcess> StartAsync(IReadOnlyList<string> extraArgs, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(extraArgs);

        var helperPath = ResolveHelperPath();

        if (!File.Exists(helperPath))
        {
            throw new FileNotFoundException(
                $"Elevation helper not found at expected path: {helperPath}. The MSIX deploy may be incomplete.",
                helperPath);
        }

        var pipeName = $"eventlogexpert-elevated-{Guid.NewGuid():N}";
        var arguments = BuildArgumentString(pipeName, extraArgs);

        // Create the server pipe BEFORE spawning the helper, so we're ready to accept its connect-back without race.
        // CurrentUserOnly restricts the DACL to the current user SID; combined with PID verification below this is a
        // narrow surface — only same-user processes can even attempt to connect, and only the spawned helper's PID
        // is accepted. Wrong-PID connections are disconnected and the listener keeps waiting (within the connect
        // window) so a same-user race attacker cannot DoS the legitimate helper.
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
                FileName = helperPath,
                Arguments = arguments,
                UseShellExecute = true, // Required for Verb=runas.
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };

            // Process.Start blocks during the UAC consent prompt. We don't apply our own timeout here — the user's
            // response time is unbounded by design. A Win32Exception with NativeErrorCode==1223 signals decline.
            helperProcess = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start returned null without throwing.");

            logger.Information($"{nameof(ElevatedHelperProcessHost)}: spawned helper PID {helperProcess.Id} (pipe={pipeName}, args=[{arguments}])");

            // Connect-back: caller's CT + 30s deadline. CancelAfter merges both into the same token via linked source.
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(s_pipeConnectTimeout);

            try
            {
                await AcceptAndVerifyClientPidAsync(pipeServer, helperProcess.Id, logger, connectCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"Elevated helper PID {helperProcess.Id} did not connect to IPC pipe within {s_pipeConnectTimeout.TotalSeconds:N0}s.");
            }

            var handle = new ElevatedHelperProcess(helperProcess, pipeServer, logger);
            helperProcess = null;  // Ownership transferred.

            return handle;
        }
        catch
        {
            // Cleanup on any failure path BEFORE the handle is returned. The pipe + spawned process would otherwise
            // leak. Surface non-already-exited kill failures as Error logs so we don't silently leak high-IL helpers.
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
                    $"{nameof(ElevatedHelperProcessHost)}: failed to kill spawned helper PID {helperProcess.Id} during error cleanup: {ex.GetType().Name}: {ex.Message}");
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
                    $"{nameof(ElevatedHelperProcessHost)}: rejected pipe connection from non-helper PID (expected {expectedPid}); continuing to wait for legitimate helper.");
            }
            else
            {
                logger.Trace($"{nameof(ElevatedHelperProcessHost)}: rejected pipe connection #{rejections} from non-helper PID.");
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

    private static string BuildArgumentString(string pipeName, IReadOnlyList<string> extraArgs)
    {
        var parts = new List<string>(extraArgs.Count + 2) { "--pipe", pipeName };
        parts.AddRange(extraArgs);

        // Quote any arg containing spaces; the helper uses standard Win32 argv parsing.
        return string.Join(' ', parts.Select(QuoteIfNeeded));
    }

    private static string QuoteIfNeeded(string arg)
    {
        if (string.IsNullOrEmpty(arg)) { return "\"\""; }

        if (arg.IndexOfAny([' ', '\t', '"']) < 0) { return arg; }

        return "\"" + arg.Replace("\"", "\\\"") + "\"";
    }

    private static string ResolveHelperPath()
    {
        // Resolve via Path.GetDirectoryName(Environment.ProcessPath) instead of AppContext.BaseDirectory, which
        // is untested for packaged MAUI apps and could resolve to a single-file bundle extraction dir under MSIX.
        // The "ElevationHelper" subdir matches the Link= in the IncludeElevationHelperInPackage MSBuild target
        // in EventLogExpert.csproj.
        var processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Environment.ProcessPath is null; cannot resolve helper path.");

        var installDir = Path.GetDirectoryName(processPath)
            ?? throw new InvalidOperationException($"Cannot derive install directory from process path: {processPath}");

        return Path.Combine(installDir, "ElevationHelper", "eventlogexpert-elevated.exe");
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint clientProcessId);
    }
}
