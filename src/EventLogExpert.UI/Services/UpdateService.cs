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

public sealed class UpdateService(
    ICurrentVersionProvider versionProvider,
    IAppTitleService appTitleService,
    IGitHubService githubService,
    IDeploymentService deploymentService,
    ITraceLogger traceLogger,
    IAlertDialogService alertDialogService) : IUpdateService
{
    public async Task CheckForUpdates(bool prereleaseVersionsEnabled, bool manualScan)
    {
        traceLogger.Trace($"{nameof(CheckForUpdates)} was called. {nameof(prereleaseVersionsEnabled)} is {prereleaseVersionsEnabled}. " +
            $"{nameof(manualScan)} is {manualScan}. {nameof(versionProvider.CurrentVersion)} is {versionProvider.CurrentVersion}.");

        GitReleaseModel? latest = null;

        if (versionProvider.IsDevBuild)
        {
            traceLogger.Trace($"{nameof(CheckForUpdates)} {nameof(versionProvider.IsDevBuild)}: {versionProvider.IsDevBuild}. Skipping update check.");
            return;
        }

        try
        {
            // Versions are based on current DateTime so this is safer than dealing with
            // stripping the v off the Version for every release
            var releases = await githubService.GetReleases();
            releases = releases.OrderByDescending(x => x.ReleaseDate).ToArray();

            traceLogger.Trace($"{nameof(CheckForUpdates)} Found the following releases:");

            foreach (var release in releases)
            {
                traceLogger.Trace($"{nameof(CheckForUpdates)}   Version: {release.Version} " +
                    $"ReleaseDate: {release.ReleaseDate} IsPrerelease: {release.IsPrerelease}");
            }

            latest = prereleaseVersionsEnabled ?
                releases.FirstOrDefault() :
                releases.FirstOrDefault(x => !x.IsPrerelease);

            if (latest is null)
            {
                traceLogger.Trace($"{nameof(CheckForUpdates)} Could not find latest release.", LogLevel.Warning);

                return;
            }

            traceLogger.Trace($"{nameof(CheckForUpdates)} Found latest release {latest.Version}. IsPrerelease: {latest.IsPrerelease}");

            // Need to drop the v off the version number provided by GitHub
            var newVersion = new Version(latest.Version.TrimStart('v'));

            traceLogger.Trace($"{nameof(CheckForUpdates)} {nameof(newVersion)} {newVersion}.");

            // Setting version to equal allows rollback if a version is pulled
            if (newVersion.CompareTo(versionProvider.CurrentVersion) == 0)
            {
                if (manualScan)
                {
                    await alertDialogService.ShowAlert("No Updates Available",
                        "You are currently running the latest version.",
                        "Ok");
                }

                return;
            }

            string? downloadPath = latest.Assets.FirstOrDefault(x => x.Name.Contains(".msix"))?.Uri;

            if (downloadPath is null)
            {
                traceLogger.Trace($"{nameof(CheckForUpdates)} Could not get asset download path.", LogLevel.Warning);

                return;
            }

            bool shouldReboot = await alertDialogService.ShowAlert("Update Available",
                "A new version has been detected, would you like to install and reload the application?",
                "Yes", "No");

            traceLogger.Trace($"{nameof(CheckForUpdates)} {nameof(shouldReboot)} is {shouldReboot} after dialog.");

            if (shouldReboot)
            {
                deploymentService.RestartNowAndUpdate(downloadPath);
            }
            else
            {
                deploymentService.UpdateOnNextRestart(downloadPath);
            }
        }
        catch (Exception ex)
        {
            await alertDialogService.ShowAlert("Update Failure",
                $"Update failed to install:\r\n{ex.Message}",
                "Ok");
        }
        finally
        {
            appTitleService.SetIsPrerelease(latest?.IsPrerelease ?? false);

            appTitleService.SetProgressString(null);
        }
    }
}
