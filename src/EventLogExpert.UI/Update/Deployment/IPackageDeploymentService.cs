// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Windows.Foundation;
using Windows.Management.Deployment;

namespace EventLogExpert.UI.Update.Deployment;

public interface IPackageDeploymentService
{
    IAsyncOperationWithProgress<DeploymentResult, DeploymentProgress> AddPackageAsync(
        Uri packageUri,
        PackageDeploymentOptions options);
}
