// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Windows.Foundation;
using Windows.Management.Deployment;

namespace EventLogExpert.Runtime.Update.Deployment;

internal sealed class PackageDeploymentService : IPackageDeploymentService
{
    public IAsyncOperationWithProgress<DeploymentResult, DeploymentProgress> AddPackageAsync(
        Uri packageUri,
        PackageDeploymentOptions options)
    {
        PackageManager packageManager = new();

        return packageManager.AddPackageByUriAsync(packageUri,
            new AddPackageOptions
            {
                ForceUpdateFromAnyVersion = options.ForceUpdateFromAnyVersion,
                ForceTargetAppShutdown = options.ForceTargetAppShutdown,
                DeferRegistrationWhenPackagesAreInUse = options.DeferRegistrationWhenPackagesAreInUse
            });
    }
}
