// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Helpers;
using EventLogExpert.Library.Models;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Windows.ApplicationModel;
using Windows.Foundation;
using Windows.Management.Deployment;

namespace EventLogExpert;

internal static class Utils
{
    private static readonly long _maxLogSize = 10 * 1024 * 1024;

    private static bool _isDevBuild;
    private static bool _isPrereleaseBuild;

    public static string DatabasePath => Path.Join(FileSystem.AppDataDirectory, "Databases");

    public static string LoggingPath => Path.Join(FileSystem.AppDataDirectory, "debug.log");

    public static string SettingsPath => Path.Join(FileSystem.AppDataDirectory, "settings.json");

    internal static async Task CheckForUpdates(bool isPrerelease, bool manualScan = false)
    {
        Version currentVersion = GetCurrentVersion();

        Trace($"{nameof(CheckForUpdates)} was called. {nameof(isPrerelease)} is {isPrerelease}. {nameof(manualScan)} is {manualScan}. {nameof(currentVersion)} is {currentVersion}.");

        GitReleaseModel? latest = null;
        bool showDialog = Application.Current?.MainPage is not null;

        if (currentVersion.Major <= 1)
        {
            _isDevBuild = true;
            Trace($"{nameof(CheckForUpdates)} {nameof(_isDevBuild)}: {_isDevBuild}. Skipping update check.");
            return;
        }

        HttpClient client = new() { BaseAddress = new Uri("https://api.github.com/"), };

        client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        client.DefaultRequestHeaders.UserAgent.TryParseAdd("request");

        var response = await client.GetAsync("/repos/microsoft/EventLogExpert/releases");

        if (response.IsSuccessStatusCode is not true)
        {
            Trace($"{nameof(CheckForUpdates)} Attempt to retrieve {response?.RequestMessage?.RequestUri} failed: {response?.StatusCode}. {nameof(showDialog)} is {showDialog}.");
            if (showDialog)
            {
                await Application.Current!.MainPage!.DisplayAlert("Update Failure",
                    $"Unable to reach download site:\r\n{response?.StatusCode}",
                    "Ok");
            }

            return;
        }

        Trace($"{nameof(CheckForUpdates)} Attempt to retrieve {response?.RequestMessage?.RequestUri} succeeded: {response?.StatusCode}.");

        try
        {
            var stream = await response.Content.ReadAsStreamAsync();
            var content = await JsonSerializer.DeserializeAsync<IEnumerable<GitReleaseModel>>(stream);

            if (content is null)
            {
                Trace($"{nameof(CheckForUpdates)} Failed to deserialize response stream.");
                if (showDialog)
                {
                    await Application.Current!.MainPage!.DisplayAlert("Update Failure",
                        "Failed to serialize GitHub releases",
                        "Ok");
                }

                return;
            }

            // Versions are based on current DateTime so this is safer than dealing with
            // stripping the v off the Version for every release
            var releases = content.OrderByDescending(x => x.ReleaseDate).ToArray();

            Trace($"{nameof(CheckForUpdates)} Found the following releases:");
            foreach (var release in releases)
            {
                Trace($"{nameof(CheckForUpdates)}   Version: {release.Version} ReleaseDate: {release.ReleaseDate} IsPrerelease: {release.IsPrerelease}");
            }

            latest = isPrerelease ?
                releases.FirstOrDefault() :
                releases.FirstOrDefault(x => !x.IsPrerelease);

            if (latest is null)
            {
                Trace($"{nameof(CheckForUpdates)} Could not find latest release.");
                if (showDialog)
                {
                    await Application.Current!.MainPage!.DisplayAlert("Update Failure",
                        "Failed to fetch latest release",
                        "Ok");
                }

                return;
            }

            Trace($"{nameof(CheckForUpdates)} Could not find latest release.");

            // Need to drop the v off the version number provided by GitHub
            var newVersion = new Version(latest.Version.TrimStart('v'));

            Trace($"{nameof(CheckForUpdates)} {nameof(newVersion)} {newVersion} equals {nameof(currentVersion)} {currentVersion}. {nameof(showDialog)} is {showDialog}. {nameof(manualScan)} is {manualScan}.");

            // Setting version to equal allows rollback if a version is pulled
            if (newVersion.CompareTo(currentVersion) == 0)
            {
                if (showDialog && manualScan)
                {
                    await Application.Current!.MainPage!.DisplayAlert("No Updates Available",
                        "You are currently running the latest version.",
                        "Ok");
                }
            }

            string? downloadPath = latest.Assets.FirstOrDefault(x => x.Name.Contains(".msix"))?.Uri;

            if (downloadPath is null)
            {
                Trace($"{nameof(CheckForUpdates)} Could not get asset download path. {nameof(showDialog)} is {showDialog}.");
                if (showDialog)
                {
                    await Application.Current!.MainPage!.DisplayAlert("Update Failure",
                        "Failed to fetch latest release",
                        "Ok");
                }

                return;
            }

            var shouldReboot = false;

            if (showDialog)
            {
                shouldReboot = await Application.Current!.MainPage!.DisplayAlert("Update Available",
                    "A new version has been detected, would you like to install and reload the application?",
                    "Yes", "No");
            }

            Trace($"{nameof(CheckForUpdates)} {nameof(shouldReboot)} is {shouldReboot} after possible dialog. {nameof(showDialog)} was {showDialog}.");

            PackageManager packageManager = new();

            IAsyncOperationWithProgress<DeploymentResult, DeploymentProgress> deployment;

            if (shouldReboot)
            {
                Trace($"{nameof(CheckForUpdates)} Calling {nameof(NativeMethods.RegisterApplicationRestart)}.");

                uint res = NativeMethods.RegisterApplicationRestart(null, NativeMethods.RestartFlags.NONE);

                if (res != 0) { return; }

                deployment = packageManager.AddPackageByUriAsync(new Uri(downloadPath),
                    new AddPackageOptions { 
                        ForceUpdateFromAnyVersion = true, 
                        ForceTargetAppShutdown = true
                    });
            }
            else
            {
                Trace($"{nameof(CheckForUpdates)} Calling {nameof(packageManager.AddPackageByUriAsync)}.");

                deployment = packageManager.AddPackageByUriAsync(new Uri(downloadPath),
                    new AddPackageOptions
                    {
                        DeferRegistrationWhenPackagesAreInUse = true, 
                        ForceUpdateFromAnyVersion = true
                    });
            }

            deployment.Progress = (result, progress) =>
            {
                MainThread.InvokeOnMainThreadAsync(() => UpdateAppTitle($"Installing: {progress.percentage}%"));
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

                        UpdateAppTitle();
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
            _isPrereleaseBuild = latest?.IsPrerelease ?? false;

            UpdateAppTitle();
        }
    }

    internal static DateTime ConvertTimeZone(this DateTime time, TimeZoneInfo? destinationTime) =>
        destinationTime is null ? time : TimeZoneInfo.ConvertTimeFromUtc(time, destinationTime);

    internal static bool HasProviderDatabases()
    {
        try
        {
            return Directory.EnumerateFiles(DatabasePath, "*.db").Any();
        }
        catch
        {
            return false;
        }
    }

    internal static void InitTracing()
    {
        // Set up tracing to a file
        var fileInfo = new FileInfo(LoggingPath);

        if (fileInfo.Exists && fileInfo.Length > _maxLogSize)
        {
            fileInfo.Delete();
        }

        System.Diagnostics.Trace.Listeners.Add(new TextWriterTraceListener(LoggingPath, "myListener"));
        System.Diagnostics.Trace.AutoFlush = true;

        var firstChanceSemaphore = new SemaphoreSlim(1);

        // Trace all exceptions
        AppDomain.CurrentDomain.FirstChanceException += (o, args) =>
        {
            // Unless we're already tracing one.
            //
            // When an instance of EventLogExpert is launched by double-clicking an evtx file
            // and no databases are present, we get into a condition where we're trying to trace
            // a first-chance exception, and the act of tracing causes another first-chance
            // exception, which we then try to trace, until we hit a stack overflow.
            if (firstChanceSemaphore.Wait(100))
            {
                Trace($"{args.Exception}");
                firstChanceSemaphore.Release();
            }
        };
    }

    internal static void Trace(string message) => System.Diagnostics.Trace.WriteLine($"{DateTime.Now:o} {Environment.CurrentManagedThreadId} {message}");

    internal static void UpdateAppTitle(string? logName = null)
    {
        if (Application.Current?.Windows.Any() is not true) { return; }

        Version currentVersion = GetCurrentVersion();

        StringBuilder title = new();

        if (logName is not null)
        {
            title.Append($"{logName} - ");
        }

        title.Append("EventLogExpert ");

        if (_isDevBuild)
        {
            title.Append("(Development)");
        }
        else if (_isPrereleaseBuild)
        {
            title.Append($"(Preview) {currentVersion}");
        }
        else
        {
            title.Append(currentVersion);
        }

        Application.Current.Windows[0].Title = title.ToString();
    }

    private static Version GetCurrentVersion()
    {
        PackageVersion packageVersion = Package.Current.Id.Version;

        Version currentVersion =
            new($"{packageVersion.Major}.{packageVersion.Minor}.{packageVersion.Build}.{packageVersion.Revision}");

        return currentVersion;
    }
}
