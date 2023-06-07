// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Helpers;
using EventLogExpert.Models;
using System.Text.Json;
using Windows.Foundation;
using Windows.Management.Deployment;

namespace EventLogExpert.Services;

public interface IUpdateService
{
    Task CheckForUpdates(bool isPrereleaseEnabled, bool manualScan = false);
}

internal class UpdateService : IUpdateService
{
    private readonly ICurrentVersionProvider _versionProvider;

    private readonly IAppTitleService _appTitleService;

    public UpdateService(ICurrentVersionProvider versionProvider, IAppTitleService appTitleService)
    {
        _versionProvider = versionProvider;
        _appTitleService = appTitleService;
    }

    public async Task CheckForUpdates(bool isPrerelease, bool manualScan = false)
    {
        Utils.Trace($"{nameof(CheckForUpdates)} was called. {nameof(isPrerelease)} is {isPrerelease}. " +
            $"{nameof(manualScan)} is {manualScan}. {nameof(_versionProvider.CurrentVersion)} is {_versionProvider.CurrentVersion}.");

        GitReleaseModel? latest = null;
        bool showDialog = Application.Current?.MainPage is not null;

        if (_versionProvider.IsDevBuild)
        {
            Utils.Trace($"{nameof(CheckForUpdates)} {nameof(_versionProvider.IsDevBuild)}: {_versionProvider.IsDevBuild}. Skipping update check.");
            return;
        }

        HttpClient client = new() { BaseAddress = new Uri("https://api.github.com/"), };

        client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        client.DefaultRequestHeaders.UserAgent.TryParseAdd("request");

        var response = await client.GetAsync("/repos/microsoft/EventLogExpert/releases");

        if (response.IsSuccessStatusCode is not true)
        {
            Utils.Trace($"{nameof(CheckForUpdates)} Attempt to retrieve {response.RequestMessage?.RequestUri} failed: {response.StatusCode}.");

            return;
        }

        Utils.Trace($"{nameof(CheckForUpdates)} Attempt to retrieve {response.RequestMessage?.RequestUri} succeeded: {response.StatusCode}.");

        try
        {
            var stream = await response.Content.ReadAsStreamAsync();
            var content = await JsonSerializer.DeserializeAsync<IEnumerable<GitReleaseModel>>(stream);

            if (content is null)
            {
                Utils.Trace($"{nameof(CheckForUpdates)} Failed to deserialize response stream.");

                return;
            }

            // Versions are based on current DateTime so this is safer than dealing with
            // stripping the v off the Version for every release
            var releases = content.OrderByDescending(x => x.ReleaseDate).ToArray();

            Utils.Trace($"{nameof(CheckForUpdates)} Found the following releases:");

            foreach (var release in releases)
            {
                Utils.Trace($"{nameof(CheckForUpdates)}   Version: {release.Version} " +
                    $"ReleaseDate: {release.ReleaseDate} IsPrerelease: {release.IsPrerelease}");
            }

            latest = isPrerelease ?
                releases.FirstOrDefault() :
                releases.FirstOrDefault(x => !x.IsPrerelease);

            if (latest is null)
            {
                Utils.Trace($"{nameof(CheckForUpdates)} Could not find latest release.");

                return;
            }

            Utils.Trace($"{nameof(CheckForUpdates)} Found latest release {latest.Version}. IsPrerelease: {latest.IsPrerelease}");

            // Need to drop the v off the version number provided by GitHub
            var newVersion = new Version(latest.Version.TrimStart('v'));

            Utils.Trace($"{nameof(CheckForUpdates)} {nameof(newVersion)} {newVersion}.");

            // Setting version to equal allows rollback if a version is pulled
            if (newVersion.CompareTo(_versionProvider.CurrentVersion) == 0)
            {
                if (showDialog && manualScan)
                {
                    await Application.Current!.MainPage!.DisplayAlert("No Updates Available",
                        "You are currently running the latest version.",
                        "Ok");
                }

                return;
            }

            string? downloadPath = latest.Assets.FirstOrDefault(x => x.Name.Contains(".msix"))?.Uri;

            if (downloadPath is null)
            {
                Utils.Trace($"{nameof(CheckForUpdates)} Could not get asset download path.");

                return;
            }

            var shouldReboot = false;

            if (showDialog)
            {
                shouldReboot = await Application.Current!.MainPage!.DisplayAlert("Update Available",
                    "A new version has been detected, would you like to install and reload the application?",
                    "Yes", "No");
            }

            Utils.Trace($"{nameof(CheckForUpdates)} {nameof(shouldReboot)} is {shouldReboot} after possible dialog. " +
                $"{nameof(showDialog)} was {showDialog}.");

            PackageManager packageManager = new();

            IAsyncOperationWithProgress<DeploymentResult, DeploymentProgress> deployment;

            if (shouldReboot)
            {
                Utils.Trace($"{nameof(CheckForUpdates)} Calling {nameof(NativeMethods.RegisterApplicationRestart)}.");

                uint res = NativeMethods.RegisterApplicationRestart(null, NativeMethods.RestartFlags.NONE);

                if (res != 0) { return; }

                deployment = packageManager.AddPackageByUriAsync(new Uri(downloadPath),
                    new AddPackageOptions
                    {
                        ForceUpdateFromAnyVersion = true,
                        ForceTargetAppShutdown = true
                    });
            }
            else
            {
                Utils.Trace($"{nameof(CheckForUpdates)} Calling {nameof(packageManager.AddPackageByUriAsync)}.");

                deployment = packageManager.AddPackageByUriAsync(new Uri(downloadPath),
                    new AddPackageOptions
                    {
                        DeferRegistrationWhenPackagesAreInUse = true,
                        ForceUpdateFromAnyVersion = true
                    });
            }

            deployment.Progress = (result, progress) =>
            {
                MainThread.InvokeOnMainThreadAsync(() => _appTitleService.SetProgressString($"Installing: {progress.percentage}%"));
            };

            deployment.Completed = (result, progress) =>
            {
                MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (result.Status is AsyncStatus.Error && showDialog)
                    {
                        Application.Current!.MainPage!.DisplayAlert("Update Failure",
                            $"Update failed to install:\r\n{result.ErrorCode}",
                            "Ok");

                        _appTitleService.SetProgressString(null);
                    }
                });
            };
        }
        catch (Exception ex)
        {
            if (showDialog)
            {
                await Application.Current!.MainPage!.DisplayAlert("Update Failure",
                    $"Update failed to install:\r\n{ex.Message}",
                    "Ok");
            }
        }
        finally
        {
            _appTitleService.SetIsPrerelease(latest?.IsPrerelease ?? false);

            _appTitleService.SetProgressString(null);
        }
    }
}
