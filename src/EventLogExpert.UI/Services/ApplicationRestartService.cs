// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;

namespace EventLogExpert.UI.Services;

public sealed class ApplicationRestartService : IApplicationRestartService
{
    public bool RegisterApplicationRestart()
    {
        uint result = NativeMethods.RegisterApplicationRestart(null, RestartFlags.NONE);
        return result == 0;
    }
}
