// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Common.AppTitle;
using EventLogExpert.Runtime.Common.Versioning;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.Runtime.Update.Deployment;
using EventLogExpert.Runtime.Update.ReleaseNotes;
using System.Runtime.InteropServices;

namespace EventLogExpert.Runtime.Update;

internal sealed class UpdateService(
    ICurrentVersionProvider versionProvider,
    IAppTitleService appTitleService,
    IGitHubService githubService,
    IDeploymentService deploymentService,
    ITraceLogger traceLogger,
    IAlertDialogService alertDialogService,
    ISettingsService settings) : IUpdateService
{
    private string? _currentRawChanges;
    private int _hasAutoChecked;

    public async Task CheckForUpdates(bool usePreRelease, bool userInitiated = false)
    {
        traceLogger.Debug($"{nameof(CheckForUpdates)} was called. {nameof(usePreRelease)} is {usePreRelease}. " +
            $"{nameof(userInitiated)} is {userInitiated}. {nameof(versionProvider.CurrentVersion)} is {versionProvider.CurrentVersion}.");

        if (!userInitiated && Interlocked.CompareExchange(ref _hasAutoChecked, 1, 0) != 0)
        {
            traceLogger.Debug($"{nameof(CheckForUpdates)} skipping automatic check; one already ran this session.");

            return;
        }

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

        GitHubRelease? latest = null;

        try
        {
            GitHubRelease[] releases = [.. (await githubService.GetReleases()).OrderByDescending(x => x.ReleaseDate)];

            if (releases.Length <= 0)
            {
                throw new FileNotFoundException("No releases available");
            }

            traceLogger.Debug($"{nameof(CheckForUpdates)} Found the following releases:");

            GitHubRelease? currentRelease = null;

            foreach (var release in releases)
            {
                traceLogger.Debug($"{nameof(CheckForUpdates)}   Version: {release.Version} " +
                    $"ReleaseDate: {release.ReleaseDate} IsPreRelease: {release.IsPreRelease}");

                if (!Version.TryParse(release.Version.TrimStart('v'), out var releaseVersion))
                {
                    traceLogger.Warning($"{nameof(CheckForUpdates)} skipping release with unparseable version: {release.Version}");

                    continue;
                }

                if (versionProvider.CurrentVersion.CompareTo(releaseVersion) == 0)
                {
                    currentRelease = release;

                    break;
                }
            }

            bool effectiveUsePreRelease = usePreRelease;

            if (currentRelease is { } current)
            {
                _currentRawChanges = current.RawChanges;

                if (current.IsPreRelease)
                {
                    appTitleService.SetIsPrerelease(true);

                    if (settings is { IsPreReleaseEnabled: false, HasEverEnabledPreRelease: false })
                    {
                        settings.IsPreReleaseEnabled = true;
                        effectiveUsePreRelease = true;

                        traceLogger.Debug($"{nameof(CheckForUpdates)} auto-enabled IsPreReleaseEnabled " +
                            $"(running prerelease {current.Version}).");
                    }
                }
            }

            foreach (var release in releases)
            {
                if (!effectiveUsePreRelease && release.IsPreRelease) { continue; }

                if (!Version.TryParse(release.Version.TrimStart('v'), out var releaseVersion))
                {
                    continue;
                }

                if (versionProvider.CurrentVersion.CompareTo(releaseVersion) != 0)
                {
                    latest = release;
                }
                else
                {
                    if (userInitiated)
                    {
                        await alertDialogService.ShowAlert("No Updates Available",
                            "You are currently running the latest version.",
                            "OK");
                    }

                    return;
                }

                break;
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
            string? downloadPath = SelectUpdateDownloadUri(latest.Value.Assets);

            if (string.IsNullOrEmpty(downloadPath))
            {
                string availableAssets = latest.Value.Assets is null or { Count: 0 }
                    ? "(none)"
                    : string.Join(", ", latest.Value.Assets.Select(asset => string.IsNullOrEmpty(asset.Name) ? "(unnamed)" : asset.Name));

                traceLogger.Warning($"{nameof(CheckForUpdates)} No update bundle (.msixbundle) was found in the " +
                    $"latest release. OS architecture: {RuntimeInformation.OSArchitecture}. Available assets: {availableAssets}");

                if (userInitiated)
                {
                    await alertDialogService.ShowAlert("Update Unavailable",
                        "No compatible update package was found.",
                        "OK");
                }

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

        var markdown = GitHubReleaseNormalizer.Normalize(_currentRawChanges);
        var title = $"Release notes for v{versionProvider.CurrentVersion}";

        return new ReleaseNotesContent(title, markdown);
    }

    /// <summary>
    ///     Selects the download URI of the multi-architecture app bundle (.msixbundle) from the release assets, or
    ///     <see langword="null" /> when the release contains no bundle.
    /// </summary>
    internal static string? SelectUpdateDownloadUri(IReadOnlyList<GitHubReleaseAsset>? assets)
    {
        return assets?.Where(asset =>
                asset.Name is not null &&
                (asset.Name.StartsWith("EventLogExpert_", StringComparison.OrdinalIgnoreCase) ||
                    asset.Name.StartsWith("EventLogExpert.", StringComparison.OrdinalIgnoreCase)) &&
                asset.Name.EndsWith(".msixbundle", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(asset.Uri))
            .Select(asset => asset.Uri)
            .FirstOrDefault();
    }
}
