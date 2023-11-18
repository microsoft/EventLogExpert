// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using System.Reflection;
using Windows.Foundation;
using Windows.Management.Deployment;

namespace EventLogExpert.UI.Services;

public interface IDeploymentService
{
    void RestartNowAndUpdate(string downloadPath);

    void UpdateOnNextRestart(string downloadPath);
}

public sealed class DeploymentService(
    ITraceLogger traceLogger,
    IAppTitleService appTitleService,
    IMainThreadService mainThreadService,
    IAlertDialogService alertDialogService) : IDeploymentService
{
    public void RestartNowAndUpdate(string downloadPath)
    {
        traceLogger.Trace($"{MethodBase.GetCurrentMethod()} Calling {nameof(NativeMethods.RegisterApplicationRestart)}.");

        uint res = NativeMethods.RegisterApplicationRestart(null, NativeMethods.RestartFlags.NONE);

        if (res != 0) { return; }

        PackageManager packageManager = new();

        traceLogger.Trace($"{MethodBase.GetCurrentMethod()} Calling {nameof(packageManager.AddPackageByUriAsync)}.");

        var deployment = packageManager.AddPackageByUriAsync(new Uri(downloadPath),
            new AddPackageOptions
            {
                ForceUpdateFromAnyVersion = true,
                ForceTargetAppShutdown = true
            });

        SetCallbacks(deployment);
    }

    public void UpdateOnNextRestart(string downloadPath)
    {
        PackageManager packageManager = new();

        traceLogger.Trace($"{MethodBase.GetCurrentMethod()} Calling {nameof(packageManager.AddPackageByUriAsync)}.");

        var deployment = packageManager.AddPackageByUriAsync(new Uri(downloadPath),
            new AddPackageOptions
            {
                DeferRegistrationWhenPackagesAreInUse = true,
                ForceUpdateFromAnyVersion = true
            });

        SetCallbacks(deployment);
    }

    private void SetCallbacks(IAsyncOperationWithProgress<DeploymentResult, DeploymentProgress> deployment)
    {
        deployment.Progress = (result, progress) =>
        {
            mainThreadService.InvokeOnMainThread(() => appTitleService.SetProgressString($"Installing: {progress.percentage}%"));
        };

        deployment.Completed = (result, progress) =>
        {
            mainThreadService.InvokeOnMainThread(() =>
            {
                switch (result.Status)
                {
                    case AsyncStatus.Error :
                        alertDialogService.ShowAlert("Update Failure",
                            $"Update failed to install:\r\n{result.ErrorCode}",
                            "Ok");

                        appTitleService.SetProgressString(null);
                        break;
                    case AsyncStatus.Completed : 
                        appTitleService.SetProgressString("Relaunch to Apply Update");
                        break;
                    case AsyncStatus.Canceled :
                    case AsyncStatus.Started :
                    default : 
                        appTitleService.SetProgressString(null);
                        break;
                }
            });
        };
    }
}
