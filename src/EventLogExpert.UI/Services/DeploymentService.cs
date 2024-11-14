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

public class DeploymentService(
    ITraceLogger traceLogger,
    IAppTitleService appTitleService,
    IMainThreadService mainThreadService,
    IAlertDialogService alertDialogService) : IDeploymentService
{
    private readonly ITraceLogger _traceLogger = traceLogger;
    private readonly IAppTitleService _appTitleService = appTitleService;
    private readonly IMainThreadService _mainThreadService = mainThreadService;
    private readonly IAlertDialogService _alertDialogService = alertDialogService;

    public void RestartNowAndUpdate(string downloadPath)
    {
        _traceLogger.Trace($"{MethodBase.GetCurrentMethod()} Calling {nameof(NativeMethods.RegisterApplicationRestart)}.");

        uint res = NativeMethods.RegisterApplicationRestart(null, RestartFlags.NONE);

        if (res != 0) { return; }

        PackageManager packageManager = new();

        _traceLogger.Trace($"{MethodBase.GetCurrentMethod()} Calling {nameof(packageManager.AddPackageByUriAsync)}.");

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

        _traceLogger.Trace($"{MethodBase.GetCurrentMethod()} Calling {nameof(packageManager.AddPackageByUriAsync)}.");

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
            _mainThreadService.InvokeOnMainThread(() => _appTitleService.SetProgressString($"Installing: {progress.percentage}%"));
        };

        deployment.Completed = (result, progress) =>
        {
            _mainThreadService.InvokeOnMainThread(() =>
            {
                switch (result.Status)
                {
                    case AsyncStatus.Error :
                        _alertDialogService.ShowAlert("Update Failure",
                            $"Update failed to install:\r\n{result.ErrorCode}",
                            "Ok");

                        _appTitleService.SetProgressString(null);
                        break;
                    case AsyncStatus.Completed : 
                        _appTitleService.SetProgressString("Relaunch to Apply Update");
                        break;
                    case AsyncStatus.Canceled :
                    case AsyncStatus.Started :
                    default : 
                        _appTitleService.SetProgressString(null);
                        break;
                }
            });
        };
    }
}
