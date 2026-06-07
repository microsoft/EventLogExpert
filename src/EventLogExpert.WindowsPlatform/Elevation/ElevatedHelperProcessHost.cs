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
    private const int PipeBufferSize = 65536;

    private static readonly TimeSpan s_pipeConnectTimeout = TimeSpan.FromSeconds(30);

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
        // is accepted.
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
                await pipeServer.WaitForConnectionAsync(connectCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"Elevated helper PID {helperProcess.Id} did not connect to IPC pipe within {s_pipeConnectTimeout.TotalSeconds:N0}s.");
            }

            // PID verification: defense-in-depth on top of the CurrentUserOnly ACL. A same-user attacker would need
            // to race a connect to this GUID-named pipe AND already be running with our exact PID — not feasible.
            VerifyClientPid(pipeServer, expectedPid: helperProcess.Id);

            var handle = new ElevatedHelperProcess(helperProcess, pipeServer, logger);
            helperProcess = null;  // Ownership transferred.

            return handle;
        }
        catch
        {
            // Cleanup on any failure path BEFORE the handle is returned. The pipe + spawned process would otherwise
            // leak. Process.Kill() on an already-exited process is a no-op (per docs).
            try { pipeServer.Dispose(); } catch { /* swallowed during error cleanup */ }

            if (helperProcess is not null)
            {
                try
                {
                    if (!helperProcess.HasExited) { helperProcess.Kill(entireProcessTree: false); }
                }
                catch { /* swallowed during error cleanup */ }

                helperProcess.Dispose();
            }

            throw;
        }
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

    private static void VerifyClientPid(NamedPipeServerStream pipeServer, int expectedPid)
    {
        if (!NativeMethods.GetNamedPipeClientProcessId(pipeServer.SafePipeHandle, out var clientPid))
        {
            var error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"GetNamedPipeClientProcessId failed (Win32 error {error}).");
        }

        if (clientPid != (uint)expectedPid)
        {
            throw new InvalidOperationException(
                $"Pipe client PID {clientPid} does not match spawned helper PID {expectedPid}. Rejecting connection.");
        }
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint clientProcessId);
    }
}
