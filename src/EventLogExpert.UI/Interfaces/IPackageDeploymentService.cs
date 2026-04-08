// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Options;
using Windows.Foundation;
using Windows.Management.Deployment;

namespace EventLogExpert.UI.Interfaces;

public interface IPackageDeploymentService
{
    IAsyncOperationWithProgress<DeploymentResult, DeploymentProgress> AddPackageAsync(
        Uri packageUri,
        PackageDeploymentOptions options);
}
