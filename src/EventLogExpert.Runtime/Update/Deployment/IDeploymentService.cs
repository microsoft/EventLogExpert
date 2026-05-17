// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Update.Deployment;

public interface IDeploymentService
{
    void RestartNowAndUpdate(string downloadPath, bool userInitiated = false);

    void UpdateOnNextRestart(string downloadPath, bool userInitiated = false);
}
