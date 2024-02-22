// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Models;
using Microsoft.Extensions.Logging;
using System.Text;

namespace EventLogExpert.UI.Services;

public interface IUpdateService
{
    Task CheckForUpdates(bool usePreRelease, bool manualScan);

    Task GetReleaseNotes();
}

public sealed class UpdateService(
    ICurrentVersionProvider versionProvider,
    IAppTitleService appTitleService,
    IGitHubService githubService,
    IDeploymentService deploymentService,
    ITraceLogger traceLogger,
    IAlertDialogService alertDialogService) : IUpdateService
{
    private List<string>? _currentChanges;

    public async Task CheckForUpdates(bool usePreRelease, bool manualScan)
    {
        traceLogger.Trace($"{nameof(CheckForUpdates)} was called. {nameof(usePreRelease)} is {usePreRelease}. " +
            $"{nameof(manualScan)} is {manualScan}. {nameof(versionProvider.CurrentVersion)} is {versionProvider.CurrentVersion}.");

        if (versionProvider.IsDevBuild)
        {
            traceLogger.Trace($"{nameof(CheckForUpdates)} {nameof(versionProvider.IsDevBuild)}: {versionProvider.IsDevBuild}. Skipping update check.");

            return;
        }

        GitReleaseModel? latest = null;

        try
        {
            // Versions are based on current DateTime so this is safer than dealing with
            // stripping the v off the Version for every release
            GitReleaseModel[] releases = [.. (await githubService.GetReleases()).OrderByDescending(x => x.ReleaseDate)];

            if (releases.Length <= 0)
            {
                throw new FileNotFoundException("No releases available");
            }

            traceLogger.Trace($"{nameof(CheckForUpdates)} Found the following releases:");

            foreach (var release in releases)
            {
                traceLogger.Trace($"{nameof(CheckForUpdates)}   Version: {release.Version} " +
                    $"ReleaseDate: {release.ReleaseDate} IsPreRelease: {release.IsPreRelease}");

                if (!usePreRelease && release.IsPreRelease) { continue; }

                // Need to drop the v off the version number provided by GitHub
                if (versionProvider.CurrentVersion.CompareTo(new Version(release.Version.TrimStart('v'))) != 0) {
                    latest = release;

                    break;
                }

                _currentChanges = release.Changes;

                if (release.IsPreRelease)
                {
                    appTitleService.SetIsPrerelease(true);
                }

                if (manualScan)
                {
                    await alertDialogService.ShowAlert("No Updates Available",
                        "You are currently running the latest version.",
                        "Ok");
                }

                return;
            }

            if (!latest.HasValue)
            {
                throw new FileNotFoundException("Unable to determine latest version");
            }
        }
        catch (Exception ex)
        {
            traceLogger.Trace($"{nameof(CheckForUpdates)} failed while retrieving releases: {ex.Message}.", LogLevel.Warning);
            
            await alertDialogService.ShowAlert("Update Failure",
                $"Failed to retrieve latest releases:\r\n{ex.Message}",
                "Ok");

            return;
        }

        traceLogger.Trace($"{nameof(CheckForUpdates)} Found latest release {latest.Value.Version}. IsPreRelease: {latest.Value.IsPreRelease}");

        try
        {
            string downloadPath = latest.Value.Assets.First(x => x.Name.Contains(".msix")).Uri;

            if (string.IsNullOrEmpty(downloadPath))
            {
                traceLogger.Trace($"{nameof(CheckForUpdates)} Could not get asset download path.", LogLevel.Warning);

                return;
            }

            bool shouldReboot = await alertDialogService.ShowAlert(
                "Update Available",
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
            appTitleService.SetProgressString(null);
        }
    }

    public async Task GetReleaseNotes()
    {
        if (_currentChanges is null)
        {
            await alertDialogService.ShowAlert("Release Notes Failure",
                "Failed to get release notes for the current version",
                "Ok");

            return;
        }

        StringBuilder formattedChanges = new();

        foreach (var change in _currentChanges)
        {
            formattedChanges.AppendLine($"# {change}");
        }

        await alertDialogService.ShowAlert($"Release notes for {versionProvider.CurrentVersion}", formattedChanges.ToString(), "Ok");
    }
}
