// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Common.Elevation;
using System.ComponentModel;
using System.Diagnostics;

namespace EventLogExpert.Adapters.Elevation;

/// <summary>
///     MAUI/Windows implementation of <see cref="IElevationService" />. Uses <c>ShellExecute</c> via
///     <see cref="ProcessStartInfo.Verb" /> = "runas" to trigger the UAC prompt. ERROR_CANCELLED (1223) from
///     <see cref="Win32Exception" /> indicates the user declined the UAC prompt — return
///     <see cref="ElevationResult.UserCancelled" /> without surfacing as a failure.
/// </summary>
internal sealed class MauiElevationService(ITraceLogger logger) : IElevationService
{
    private const int ERROR_CANCELLED = 1223;

    public Task<ElevationResult> RelaunchElevatedAsync(string? launchArguments = null)
    {
        var processPath = Environment.ProcessPath;

        if (string.IsNullOrEmpty(processPath))
        {
            logger.Error($"{nameof(MauiElevationService)}: Environment.ProcessPath was null or empty; cannot relaunch.");

            return Task.FromResult(ElevationResult.Failed);
        }

        var psi = new ProcessStartInfo
        {
            FileName = processPath,
            Arguments = launchArguments ?? string.Empty,
            UseShellExecute = true,
            Verb = "runas"
        };

        try
        {
            var process = Process.Start(psi);

            return Task.FromResult(process is null ? ElevationResult.Failed : ElevationResult.Relaunched);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ERROR_CANCELLED)
        {
            return Task.FromResult(ElevationResult.UserCancelled);
        }
        catch (Exception ex)
        {
            logger.Error($"{nameof(MauiElevationService)}.{nameof(RelaunchElevatedAsync)} threw: {ex}");

            return Task.FromResult(ElevationResult.Failed);
        }
    }
}
