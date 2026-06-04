// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.DatabaseTools.Elevation;

/// <summary>
///     Platform abstraction for launching the packaged elevation-helper EXE under UAC and connecting to it via a
///     same-user duplex named pipe. The MAUI head provides the concrete <c>ShellExecute Verb="runas"</c> implementation;
///     tests substitute scripted fakes so the runner logic can be exercised without spawning a real process and without
///     surfacing a UAC prompt.
/// </summary>
public interface IElevatedHelperProcessHost
{
    /// <summary>
    ///     Spawns the helper EXE, awaits its connect-back on the host-owned named pipe, verifies the connecting client
    ///     process id matches the spawned process (defense-in-depth on top of the pipe's
    ///     <see cref="System.IO.Pipes.PipeOptions.CurrentUserOnly" /> ACL), and returns a handle exposing the duplex pipe
    ///     stream + process lifetime hooks. Implementations MUST surface a UAC decline as
    ///     <see cref="System.ComponentModel.Win32Exception" /> with <c>NativeErrorCode == 1223</c> so the caller can translate
    ///     it to <see cref="EventLogExpert.DatabaseTools.Common.Operations.DatabaseToolsOutcome.Cancelled" /> rather than as a
    ///     generic failure.
    /// </summary>
    /// <param name="extraArgs">
    ///     Additional CLI arguments passed to the helper after the host-supplied
    ///     <c>--pipe &lt;name&gt;</c> argument. Empty in normal operation mode (helper reads the request from the pipe);
    ///     <c>["--probe"]</c> for the probe diagnostic mode.
    /// </param>
    /// <param name="cancellationToken">Cancels the spawn + pipe-connect + PID-verification flow.</param>
    Task<IElevatedHelperProcess> StartAsync(IReadOnlyList<string> extraArgs, CancellationToken cancellationToken);
}
