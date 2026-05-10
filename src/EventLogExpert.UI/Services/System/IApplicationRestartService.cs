// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Interfaces;

public interface IApplicationRestartService
{
    /// <summary>Registers the application for restart.</summary>
    /// <returns>True if registration was successful (return code 0), false otherwise.</returns>
    bool RegisterApplicationRestart();

    Task<bool> TryRestartAsync(string launchArguments = "");
}
