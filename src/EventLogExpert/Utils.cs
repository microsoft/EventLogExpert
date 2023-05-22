// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Helpers;
using EventLogExpert.Library.Models;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Windows.ApplicationModel;
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

    internal static async Task<bool> CheckForUpdates(bool isPrerelease)
    {
        Version currentVersion = GetCurrentVersion();

        if (currentVersion.Major <= 1)
        {
            _isDevBuild = true;
            return false;
        }

        HttpClient client = new() { BaseAddress = new Uri("https://api.github.com/"), };

        client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        client.DefaultRequestHeaders.UserAgent.TryParseAdd("request");

        var response = await client.GetAsync("/repos/microsoft/EventLogExpert/releases");

        if (response.IsSuccessStatusCode is not true) { return false; }

        try
        {
            var stream = await response.Content.ReadAsStreamAsync();
            var content = await JsonSerializer.DeserializeAsync<IEnumerable<GitReleaseModel>>(stream);

            if (content is null) { return false; }

            // Versions are based on current DateTime so this is safer than dealing with
            // stripping the v off the Version for every release
            var releases = content.OrderByDescending(x => x.ReleaseDate).ToArray();

            GitReleaseModel? latest = isPrerelease ?
                releases.FirstOrDefault(x => x.IsPrerelease) :
                releases.FirstOrDefault();

            if (latest is null) { return false; }

            _isPrereleaseBuild = latest.IsPrerelease;

            // Need to drop the v off the version number provided by GitHub
            var newVersion = new Version(latest.Version.TrimStart('v'));

            // Equality comparison allows us to downgrade in the event that we pull a release
            if (newVersion.CompareTo(currentVersion) == 0) { return false; }

            string? downloadPath = latest.Assets.FirstOrDefault(x => x.Name.Contains(".msix"))?.Uri;

            if (downloadPath is null) { return false; }

            uint res = NativeMethods.RegisterApplicationRestart(null, NativeMethods.RestartFlags.NONE);

            if (res != 0) { return false; }

            if (Application.Current?.MainPage is not null)
            {
                await Application.Current.MainPage.DisplayAlert("Update Available",
                    "A new version has been detected, app will restart shortly.",
                    "Ok");
            }

            PackageManager packageManager = new();

            await packageManager.AddPackageAsync(new Uri(downloadPath),
                null,
                DeploymentOptions.ForceTargetApplicationShutdown);
        }
        catch
        { // TODO: Log Update Failure
            return false;
        }

        return true;
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

        // Trace all exceptions
        AppDomain.CurrentDomain.FirstChanceException += (o, args) =>
        {
            Trace($"{args.Exception}");
        };
    }

    internal static void Trace(string message) => System.Diagnostics.Trace.WriteLine($"{DateTime.Now:o} {message}");

    internal static void UpdateAppTitle(string? logName = null)
    {
        if (Application.Current?.Windows.Any() is not true) { return; }

        Version currentVersion = GetCurrentVersion();

        StringBuilder title = new("EventLogExpert ");

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

        if (logName is not null)
        {
            title.Append($" {logName}");
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
