// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Options;
using System.Reflection;
using Windows.Foundation;
using Windows.Management.Deployment;

namespace EventLogExpert.UI.Services;

public class DeploymentService(
    ITraceLogger traceLogger,
    IAppTitleService appTitleService,
    IMainThreadService mainThreadService,
    IAlertDialogService alertDialogService,
    IApplicationRestartService applicationRestartService,
    IPackageDeploymentService packageDeploymentService) : IDeploymentService
{
    private readonly IAlertDialogService _alertDialogService = alertDialogService;
    private readonly IApplicationRestartService _applicationRestartService = applicationRestartService;
    private readonly IAppTitleService _appTitleService = appTitleService;
    private readonly IMainThreadService _mainThreadService = mainThreadService;
    private readonly IPackageDeploymentService _packageDeploymentService = packageDeploymentService;
    private readonly ITraceLogger _traceLogger = traceLogger;

    public void RestartNowAndUpdate(string downloadPath)
    {
        _traceLogger.Trace($"{MethodBase.GetCurrentMethod()} Calling {nameof(_applicationRestartService.RegisterApplicationRestart)}.");

        bool registrationSuccessful = _applicationRestartService.RegisterApplicationRestart();

        if (!registrationSuccessful) { return; }

        _traceLogger.Trace($"{MethodBase.GetCurrentMethod()} Calling {nameof(_packageDeploymentService.AddPackageAsync)}.");

        var deployment = _packageDeploymentService.AddPackageAsync(
            new Uri(downloadPath),
            new PackageDeploymentOptions(ForceUpdateFromAnyVersion: true, ForceTargetAppShutdown: true));

        SetCallbacks(deployment);
    }

    public void UpdateOnNextRestart(string downloadPath)
    {
        _traceLogger.Trace($"{MethodBase.GetCurrentMethod()} Calling {nameof(_packageDeploymentService.AddPackageAsync)}.");

        var deployment = _packageDeploymentService.AddPackageAsync(
            new Uri(downloadPath),
            new PackageDeploymentOptions(ForceUpdateFromAnyVersion: true, DeferRegistrationWhenPackagesAreInUse: true));

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
