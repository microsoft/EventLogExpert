// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Common.Elevation;

/// <summary>
///     Abstracts the platform-specific mechanism for relaunching the current process with administrator privileges.
///     Decoupled from the MAUI head so UI tabs can be unit-tested against a mocked implementation rather than the real
///     <see cref="System.Diagnostics.Process.Start(System.Diagnostics.ProcessStartInfo)" /> + <c>runas</c> path.
/// </summary>
public interface IElevationService
{
    /// <summary>
    ///     Attempts to start a new elevated instance of the current process. On <see cref="ElevationResult.Relaunched" />
    ///     the caller should exit the current (non-elevated) instance; on other outcomes the caller should leave the current
    ///     instance running and surface an inline error to the user.
    /// </summary>
    /// <param name="launchArguments">Optional command-line arguments to pass to the elevated instance.</param>
    Task<ElevationResult> RelaunchElevatedAsync(string? launchArguments = null);
}
