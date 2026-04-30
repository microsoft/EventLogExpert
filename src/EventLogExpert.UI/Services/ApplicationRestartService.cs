// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Interfaces;
using Windows.ApplicationModel.Core;

namespace EventLogExpert.UI.Services;

public sealed class ApplicationRestartService(ITraceLogger traceLogger) : IApplicationRestartService
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
                _traceLogger.Info($"{nameof(ApplicationRestartService)}.{nameof(TryRestartAsync)}: restart already pending");
                return true;
            }

            _traceLogger.Error($"{nameof(ApplicationRestartService)}.{nameof(TryRestartAsync)}: restart denied: {reason}");
            return false;
        }
        catch (Exception ex)
        {
            _traceLogger.Error($"{nameof(ApplicationRestartService)}.{nameof(TryRestartAsync)}: restart threw: {ex}");
            return false;
        }
    }
}
