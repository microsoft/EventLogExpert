// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Update.Deployment;

public interface IDeploymentService
{
    void RestartNowAndUpdate(string downloadPath, bool userInitiated = false);

    void UpdateOnNextRestart(string downloadPath, bool userInitiated = false);
}
