// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Common.Restart;
using Windows.ApplicationModel.Core;

namespace EventLogExpert.WindowsPlatform.Restart;

internal sealed class WindowsApplicationRestartService(ITraceLogger traceLogger) : IApplicationRestartService
{
    private readonly ITraceLogger _traceLogger = traceLogger;

    public bool RegisterApplicationRestart()
    {
        uint result = NativeMethods.RegisterApplicationRestart(null, RestartFlags.NONE);

        return result == 0;
    }

    public async Task<bool> TryRestartAsync(string launchArguments = "")
    {
        try
        {
            // Successful restart terminates process; only failures and pending restarts return here.
            AppRestartFailureReason reason = await CoreApplication.RequestRestartAsync(launchArguments);

            if (reason == AppRestartFailureReason.RestartPending)
            {
                _traceLogger.Information($"{nameof(WindowsApplicationRestartService)}.{nameof(TryRestartAsync)}: restart already pending");
                return true;
            }

            _traceLogger.Error($"{nameof(WindowsApplicationRestartService)}.{nameof(TryRestartAsync)}: restart denied: {reason}");

            return false;
        }
        catch (Exception ex)
        {
            _traceLogger.Error($"{nameof(WindowsApplicationRestartService)}.{nameof(TryRestartAsync)}: restart threw: {ex}");

            return false;
        }
    }
}
