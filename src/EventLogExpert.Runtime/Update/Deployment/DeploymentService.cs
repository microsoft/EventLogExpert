// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
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
        _traceLogger.Debug($"{MethodBase.GetCurrentMethod()} Calling {nameof(_packageDeploymentService.AddPackageAsync)}.");

        var deployment = _packageDeploymentService.AddPackageAsync(
            new Uri(downloadPath),
            new PackageDeploymentOptions(ForceUpdateFromAnyVersion: true, DeferRegistrationWhenPackagesAreInUse: true));

        SetCallbacks(deployment, userInitiated, restartWhenComplete: true);
    }

    public void UpdateOnNextRestart(string downloadPath, bool userInitiated = false)
    {
        _traceLogger.Debug($"{MethodBase.GetCurrentMethod()} Calling {nameof(_packageDeploymentService.AddPackageAsync)}.");

        var deployment = _packageDeploymentService.AddPackageAsync(
            new Uri(downloadPath),
            new PackageDeploymentOptions(ForceUpdateFromAnyVersion: true, DeferRegistrationWhenPackagesAreInUse: true));

        SetCallbacks(deployment, userInitiated, restartWhenComplete: false);
    }

    private async Task CompleteStagedUpdateAsync(bool restartWhenComplete)
    {
        _appTitleService.SetProgress(new AppTitleProgress.RelaunchToApply());

        if (!restartWhenComplete) { return; }

        bool restartRequested = await _applicationRestartService.TryRestartAsync();

        if (!restartRequested)
        {
            _traceLogger.Warning($"{nameof(DeploymentService)} graceful restart was not honored; the staged update will apply the next time the app is launched.");
        }
    }

    private void SetCallbacks(
        IAsyncOperationWithProgress<DeploymentResult, DeploymentProgress> deployment,
        bool userInitiated,
        bool restartWhenComplete)
    {
        deployment.Progress = (result, progress) =>
        {
            _mainThreadService.InvokeOnMainThread(() => _appTitleService.SetProgress(new AppTitleProgress.Installing((int)progress.percentage)));
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
                            await _alertDialogService.ShowAlert(
                                new LocalizableText("Update_Alert_Failure_Title"),
                                new LocalizableText("Update_Alert_InstallFailed_Message", [$"{result.ErrorCode}"]),
                                new LocalizableText("Modal_Accept"));
                        }

                        _appTitleService.SetProgress(null);
                        break;
                    case AsyncStatus.Completed:
                        await CompleteStagedUpdateAsync(restartWhenComplete);
                        break;
                    case AsyncStatus.Canceled:
                    case AsyncStatus.Started:
                    default:
                        _appTitleService.SetProgress(null);
                        break;
                }
            });

            completionTask.ContinueWith(
                t => _traceLogger.Error($"{nameof(DeploymentService)} deployment completion handler failed: {t.Exception}"),
                TaskContinuationOptions.OnlyOnFaulted);
        };
    }
}
