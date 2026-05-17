// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Logging;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Common.AppTitle;
using EventLogExpert.Runtime.Common.Restart;
using EventLogExpert.Runtime.Common.Threading;
using System.Reflection;
using Windows.Foundation;
using Windows.Management.Deployment;

namespace EventLogExpert.Runtime.Update.Deployment;

internal sealed class DeploymentService(
    ITraceLogger traceLogger,
    IAppTitleService appTitleService,
    IMainThreadService mainThreadService,
    IAlertDialogService alertDialogService,
    IApplicationRestartService applicationRestartService,
    IPackageDeploymentService packageDeploymentService) : IDeploymentService
{
    private readonly IAlertDialogService _alertDialogService = alertDialogService;
    private readonly IAppTitleService _appTitleService = appTitleService;
    private readonly IApplicationRestartService _applicationRestartService = applicationRestartService;
    private readonly IMainThreadService _mainThreadService = mainThreadService;
    private readonly IPackageDeploymentService _packageDeploymentService = packageDeploymentService;
    private readonly ITraceLogger _traceLogger = traceLogger;

    public void RestartNowAndUpdate(string downloadPath, bool userInitiated = false)
    {
        _traceLogger.Debug($"{MethodBase.GetCurrentMethod()} Calling {nameof(_applicationRestartService.RegisterApplicationRestart)}.");

        bool registrationSuccessful = _applicationRestartService.RegisterApplicationRestart();

        if (!registrationSuccessful) { return; }

        _traceLogger.Debug($"{MethodBase.GetCurrentMethod()} Calling {nameof(_packageDeploymentService.AddPackageAsync)}.");

        var deployment = _packageDeploymentService.AddPackageAsync(
            new Uri(downloadPath),
            new PackageDeploymentOptions(ForceUpdateFromAnyVersion: true, ForceTargetAppShutdown: true));

        SetCallbacks(deployment, userInitiated);
    }

    public void UpdateOnNextRestart(string downloadPath, bool userInitiated = false)
    {
        _traceLogger.Debug($"{MethodBase.GetCurrentMethod()} Calling {nameof(_packageDeploymentService.AddPackageAsync)}.");

        var deployment = _packageDeploymentService.AddPackageAsync(
            new Uri(downloadPath),
            new PackageDeploymentOptions(ForceUpdateFromAnyVersion: true, DeferRegistrationWhenPackagesAreInUse: true));

        SetCallbacks(deployment, userInitiated);
    }

    private void SetCallbacks(IAsyncOperationWithProgress<DeploymentResult, DeploymentProgress> deployment, bool userInitiated)
    {
        deployment.Progress = (result, progress) =>
        {
            _mainThreadService.InvokeOnMainThread(() => _appTitleService.SetProgressString($"Installing: {progress.percentage}%"));
        };

        deployment.Completed = (result, progress) =>
        {
            var completionTask = _mainThreadService.InvokeOnMainThreadAsync(async () =>
            {
                switch (result.Status)
                {
                    case AsyncStatus.Error:
                        if (userInitiated)
                        {
                            await _alertDialogService.ShowAlert("Update Failure",
                                $"Update failed to install:\r\n{result.ErrorCode}",
                                "Ok");
                        }

                        _appTitleService.SetProgressString(null);
                        break;
                    case AsyncStatus.Completed:
                        _appTitleService.SetProgressString("Relaunch to Apply Update");
                        break;
                    case AsyncStatus.Canceled:
                    case AsyncStatus.Started:
                    default:
                        _appTitleService.SetProgressString(null);
                        break;
                }
            });

            completionTask.ContinueWith(
                t => _traceLogger.Error($"{nameof(DeploymentService)} deployment completion handler failed: {t.Exception}"),
                TaskContinuationOptions.OnlyOnFaulted);
        };
    }
}
