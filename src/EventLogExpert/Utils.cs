// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Helpers;
using EventLogExpert.Library.Models;
using System.Diagnostics;
using System.Text.Json;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace EventLogExpert;

internal static class Utils
{
    private static readonly long _maxLogSize = 10 * 1024 * 1024;

    public static string DatabasePath => Path.Join(FileSystem.AppDataDirectory, "Databases");

    public static string LoggingPath => Path.Join(FileSystem.AppDataDirectory, "debug.log");

    public static string SettingsPath => Path.Join(FileSystem.AppDataDirectory, "settings.json");

    internal static async void CheckForUpdates()
    {
        PackageVersion packageVersion = Package.Current.Id.Version;

        Version currentVersion =
            new($"{packageVersion.Major}.{packageVersion.Minor}.{packageVersion.Build}.{packageVersion.Revision}");

        if (currentVersion.Major <= 1) { return; }

        HttpClient client = new() { BaseAddress = new Uri("https://api.github.com/"), };

        client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        client.DefaultRequestHeaders.UserAgent.TryParseAdd("request");

        // TODO: Currently only "pre release" is available, can swith to releases/latest once offical releases are out
        // Only a single release will be available, no need to index into 0 for latest
        // This will also allow us to force an install if we need to rollback a version
        var response = await client.GetAsync("/repos/microsoft/EventLogExpert/releases");

        if (response.IsSuccessStatusCode is not true) { return; }

        try
        {
            var stream = await response.Content.ReadAsStreamAsync();
            var content = await JsonSerializer.DeserializeAsync<List<GitReleaseModel>>(stream);

            if (content is null) { return; }

            // Need to drop the v off the version number provided by GitHub
            var newVersion = new Version(content[0].Version.Remove(0, 1));

            // Equality comparison allows us to downgrade in the event that we pull a release
            if (newVersion.CompareTo(currentVersion) == 0) { return; }

            if (Application.Current?.MainPage is not null)
            {
                await Application.Current.MainPage.DisplayAlert("Update Available",
                    "A new version has been detected, app will restart shortly.",
                    "Ok");
            }

            uint res = NativeMethods.RegisterApplicationRestart(null, NativeMethods.RestartFlags.NONE);

            if (res != 0) { return; }

            PackageManager packageManager = new();

            // Assets[1] should contain the MSIX
            // TODO: Add Logic to validate that content[0].Assets.Where Name contains .msix
            await packageManager.AddPackageAsync(new Uri(content[0].Assets[1].Uri),
                null,
                DeploymentOptions.ForceTargetApplicationShutdown);
        }
        catch
        { // TODO: Log Update Failure
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

        // Trace all exceptions
        AppDomain.CurrentDomain.FirstChanceException += (o, args) =>
        {
            Trace($"{args.Exception}");
        };
    }

    internal static void Trace(string message) => System.Diagnostics.Trace.WriteLine($"{DateTime.Now:o} {message}");

    internal static void UpdateAppTitle(string? title = null)
    {
        if (Application.Current?.Windows.Any() is not true) { return; }

        PackageVersion packageVersion = Package.Current.Id.Version;

        Version currentVersion =
            new($"{packageVersion.Major}.{packageVersion.Minor}.{packageVersion.Build}.{packageVersion.Revision}");

        Application.Current.Windows[0].Title = title is null ?
            $"EventLogExpert {currentVersion}" :
            $"EventLogExpert {currentVersion} {title}";
    }
}
