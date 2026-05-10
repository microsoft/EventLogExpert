// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Interfaces;

public interface IDeploymentService
{
    void RestartNowAndUpdate(string downloadPath, bool userInitiated = false);

    void UpdateOnNextRestart(string downloadPath, bool userInitiated = false);
}
