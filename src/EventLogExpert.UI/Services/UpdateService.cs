// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;

namespace EventLogExpert.UI.Services;

public readonly record struct ReleaseNotesContent(string Title, string Markdown);

public interface IUpdateService
{
    Task CheckForUpdates(bool usePreRelease, bool userInitiated = false);

    Task<ReleaseNotesContent?> GetReleaseNotes();
}

public sealed class UpdateService(
    ICurrentVersionProvider versionProvider,
    IAppTitleService appTitleService,
    IGitHubService githubService,
    IDeploymentService deploymentService,
    ITraceLogger traceLogger,
    IAlertDialogService alertDialogService) : IUpdateService
{
    private string? _currentRawChanges;

    public async Task CheckForUpdates(bool usePreRelease, bool userInitiated = false)
    {
        traceLogger.Debug($"{nameof(CheckForUpdates)} was called. {nameof(usePreRelease)} is {usePreRelease}. " +
            $"{nameof(userInitiated)} is {userInitiated}. {nameof(versionProvider.CurrentVersion)} is {versionProvider.CurrentVersion}.");

        if (versionProvider.IsDevBuild)
        {
            traceLogger.Debug($"{nameof(CheckForUpdates)} {nameof(versionProvider.IsDevBuild)}: {versionProvider.IsDevBuild}. Skipping update check.");

            if (userInitiated)
            {
                await alertDialogService.ShowAlert("Update Check Unavailable",
                    "Update checks are disabled for development builds.",
                    "OK");
            }

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

            traceLogger.Debug($"{nameof(CheckForUpdates)} Found the following releases:");

            foreach (var release in releases)
            {
                traceLogger.Debug($"{nameof(CheckForUpdates)}   Version: {release.Version} " +
                    $"ReleaseDate: {release.ReleaseDate} IsPreRelease: {release.IsPreRelease}");

                if (!usePreRelease && release.IsPreRelease) { continue; }

                // Need to drop the v off the version number provided by GitHub
                if (versionProvider.CurrentVersion.CompareTo(new Version(release.Version.TrimStart('v'))) != 0) {
                    latest = release;

                    break;
                }

                _currentRawChanges = release.RawChanges;

                if (release.IsPreRelease)
                {
                    appTitleService.SetIsPrerelease(true);
                }

                if (userInitiated)
                {
                    await alertDialogService.ShowAlert("No Updates Available",
                        "You are currently running the latest version.",
                        "OK");
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
            traceLogger.Error($"{nameof(CheckForUpdates)} failed while retrieving releases: {ex.Message}.");

            if (userInitiated)
            {
                await alertDialogService.ShowAlert("Update Failure",
                    $"Failed to retrieve latest releases:\r\n{ex.Message}",
                    "OK");
            }

            return;
        }

        traceLogger.Debug($"{nameof(CheckForUpdates)} Found latest release {latest.Value.Version}. IsPreRelease: {latest.Value.IsPreRelease}");

        bool shouldReboot = false;

        try
        {
            string downloadPath = latest.Value.Assets.First(x => x.Name.Contains(".msix")).Uri;

            if (string.IsNullOrEmpty(downloadPath))
            {
                traceLogger.Warn($"{nameof(CheckForUpdates)} Could not get asset download path.");

                return;
            }

            shouldReboot = await alertDialogService.ShowAlert(
                "Update Available",
                "A new version has been detected, would you like to install and reload the application?",
                "Yes", "No");

            traceLogger.Trace($"{nameof(CheckForUpdates)} {nameof(shouldReboot)} is {shouldReboot} after dialog.");

            if (shouldReboot)
            {
                deploymentService.RestartNowAndUpdate(downloadPath, userInitiated: true);
            }
            else
            {
                deploymentService.UpdateOnNextRestart(downloadPath, userInitiated);
            }
        }
        catch (Exception ex)
        {
            traceLogger.Error($"{nameof(CheckForUpdates)} failed while installing update: {ex}");

            if (userInitiated || shouldReboot)
            {
                await alertDialogService.ShowAlert("Update Failure",
                    $"Update failed to install:\r\n{ex.Message}",
                    "OK");
            }
        }
        finally
        {
            appTitleService.SetProgressString(null);
        }
    }

    public async Task<ReleaseNotesContent?> GetReleaseNotes()
    {
        if (string.IsNullOrWhiteSpace(_currentRawChanges))
        {
            await alertDialogService.ShowAlert("Release Notes Failure",
                "Failed to get release notes for the current version",
                "OK");

            return null;
        }

        var markdown = ReleaseNotesNormalizer.Normalize(_currentRawChanges);
        var title = $"Release notes for v{versionProvider.CurrentVersion}";

        return new ReleaseNotesContent(title, markdown);
    }
}
