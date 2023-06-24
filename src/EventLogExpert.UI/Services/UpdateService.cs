// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Models;
using Microsoft.Extensions.Logging;
using Windows.Foundation;
using Windows.Management.Deployment;

namespace EventLogExpert.UI.Services;

public interface IUpdateService
{
    Task CheckForUpdates(bool isPrereleaseEnabled, bool manualScan);
}

public class UpdateService : IUpdateService
{
    private readonly ICurrentVersionProvider _versionProvider;
    private readonly IAppTitleService _appTitleService;
    private readonly ITraceLogger _traceLogger;
    private readonly IGitHubService _gitHubService;
    private readonly IDeploymentService _deploymentService;
    private readonly IAlertDialogService _alertDialogService;
    private readonly IMainThreadService _mainThreadService;

    public UpdateService(ICurrentVersionProvider versionProvider, IAppTitleService appTitleService, IGitHubService githubService,
        IDeploymentService deploymentService, ITraceLogger traceLogger, IAlertDialogService alertDialogService, IMainThreadService mainThreadService)
    {
        _versionProvider = versionProvider;
        _appTitleService = appTitleService;
        _traceLogger = traceLogger;
        _gitHubService = githubService;
        _deploymentService = deploymentService;
        _alertDialogService = alertDialogService;
        _mainThreadService = mainThreadService;
    }

    public async Task CheckForUpdates(bool prereleaseVersionsEnabled, bool manualScan)
    {
        _traceLogger.Trace($"{nameof(CheckForUpdates)} was called. {nameof(prereleaseVersionsEnabled)} is {prereleaseVersionsEnabled}. " +
            $"{nameof(manualScan)} is {manualScan}. {nameof(_versionProvider.CurrentVersion)} is {_versionProvider.CurrentVersion}.", LogLevel.Trace);

        GitReleaseModel? latest = null;

        if (_versionProvider.IsDevBuild)
        {
            _traceLogger.Trace($"{nameof(CheckForUpdates)} {nameof(_versionProvider.IsDevBuild)}: {_versionProvider.IsDevBuild}. Skipping update check.", LogLevel.Debug);
            return;
        }

        try
        {
            // Versions are based on current DateTime so this is safer than dealing with
            // stripping the v off the Version for every release
            var releases = await _gitHubService.GetReleases();
            releases = releases.OrderByDescending(x => x.ReleaseDate).ToArray();

            _traceLogger.Trace($"{nameof(CheckForUpdates)} Found the following releases:", LogLevel.Debug);

            foreach (var release in releases)
            {
                _traceLogger.Trace($"{nameof(CheckForUpdates)}   Version: {release.Version} " +
                    $"ReleaseDate: {release.ReleaseDate} IsPrerelease: {release.IsPrerelease}", LogLevel.Debug);
            }

            latest = prereleaseVersionsEnabled ?
                releases.FirstOrDefault() :
                releases.FirstOrDefault(x => !x.IsPrerelease);

            if (latest is null)
            {
                _traceLogger.Trace($"{nameof(CheckForUpdates)} Could not find latest release.", LogLevel.Warning);

                return;
            }

            _traceLogger.Trace($"{nameof(CheckForUpdates)} Found latest release {latest.Version}. IsPrerelease: {latest.IsPrerelease}", LogLevel.Debug);

            // Need to drop the v off the version number provided by GitHub
            var newVersion = new Version(latest.Version.TrimStart('v'));

            _traceLogger.Trace($"{nameof(CheckForUpdates)} {nameof(newVersion)} {newVersion}.", LogLevel.Debug);

            // Setting version to equal allows rollback if a version is pulled
            if (newVersion.CompareTo(_versionProvider.CurrentVersion) == 0)
            {
                if (manualScan)
                {
                    await _alertDialogService.ShowAlert("No Updates Available",
                        "You are currently running the latest version.",
                        "Ok");
                }

                return;
            }

            string? downloadPath = latest.Assets.FirstOrDefault(x => x.Name.Contains(".msix"))?.Uri;

            if (downloadPath is null)
            {
                _traceLogger.Trace($"{nameof(CheckForUpdates)} Could not get asset download path.", LogLevel.Warning);

                return;
            }

            bool shouldReboot = await _alertDialogService.ShowAlert("Update Available",
                "A new version has been detected, would you like to install and reload the application?",
                "Yes", "No");

            _traceLogger.Trace($"{nameof(CheckForUpdates)} {nameof(shouldReboot)} is {shouldReboot} after dialog.", LogLevel.Debug);

            if (shouldReboot)
            {
                _deploymentService.RestartNowAndUpdate(downloadPath);
            }
            else
            {
                _deploymentService.UpdateOnNextRestart(downloadPath);
            }
        }
        catch (Exception ex)
        {
            await _alertDialogService.ShowAlert("Update Failure",
                $"Update failed to install:\r\n{ex.Message}",
                "Ok");
        }
        finally
        {
            _appTitleService.SetIsPrerelease(latest?.IsPrerelease ?? false);

            _appTitleService.SetProgressString(null);
        }
    }
}
