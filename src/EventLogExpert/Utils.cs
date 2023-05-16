// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace EventLogExpert;

internal static class Utils
{
    private static readonly long _maxLogSize = 10 * 1024 * 1024;

#region Restart Manager Enums

    /// <summary>Flags for the RegisterApplicationRestart function</summary>
    [Flags]
    internal enum RestartFlags
    {
        /// <summary>None of the options below.</summary>
        NONE = 0,

        /// <summary>Do not restart the process if it terminates due to an unhandled exception.</summary>
        RESTART_NO_CRASH = 1,
        /// <summary>Do not restart the process if it terminates due to the application not responding.</summary>
        RESTART_NO_HANG = 2,
        /// <summary>Do not restart the process if it terminates due to the installation of an update.</summary>
        RESTART_NO_PATCH = 4,
        /// <summary>Do not restart the process if the computer is restarted as the result of an update.</summary>
        RESTART_NO_REBOOT = 8
    }

#endregion Restart Manager Enums

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

            // TODO: Change this to == since we want to force an update anytime we are not on the latest
            // This will help us force a rollback if ever needed
            if (newVersion.CompareTo(currentVersion) <= 0) { return; }

            await Application.Current.MainPage.DisplayAlert("Update Available",
                "A new version has been detected, app will restart shortly.",
                "Ok");

            uint res = RegisterApplicationRestart(null, RestartFlags.NONE);

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

#region Restart Manager Methods

    // https://learn.microsoft.com/en-us/windows/msix/non-store-developer-updates

    /// <summary>Registers the active instance of an application for restart.</summary>
    /// <param name="pwzCommandLine">
    ///     A pointer to a Unicode string that specifies the command-line arguments for the
    ///     application when it is restarted. The maximum size of the command line that you can specify is RESTART_MAX_CMD_LINE
    ///     characters. Do not include the name of the executable in the command line; this function adds it for you. If this
    ///     parameter is NULL or an empty string, the previously registered command line is removed. If the argument contains
    ///     spaces, use quotes around the argument.
    /// </param>
    /// <param name="dwFlags">One of the options specified in RestartFlags</param>
    /// <returns>
    ///     This function returns S_OK on success or one of the following error codes: E_FAIL for internal error.
    ///     E_INVALIDARG if rhe specified command line is too long.
    /// </returns>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern uint RegisterApplicationRestart(string? pwzCommandLine, RestartFlags dwFlags);

#endregion Restart Manager Methods

    internal static void Trace(string message) => System.Diagnostics.Trace.WriteLine($"{DateTime.Now:o} {message}");
}
