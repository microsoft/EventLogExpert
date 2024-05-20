// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Security.Principal;
using Windows.ApplicationModel;

namespace EventLogExpert.UI.Services;

public interface ICurrentVersionProvider
{
    Version CurrentVersion { get; }

    bool IsAdmin { get; }

    bool IsDevBuild { get; }

    bool IsSupportedOS(Version currentVersion);
}

public class CurrentVersionProvider : ICurrentVersionProvider
{
    public CurrentVersionProvider()
    {
        PackageVersion packageVersion = Package.Current.Id.Version;
        CurrentVersion = new($"{packageVersion.Major}.{packageVersion.Minor}.{packageVersion.Build}.{packageVersion.Revision}");
    }

    public Version CurrentVersion { get; init; }

    public bool IsAdmin
    {
        get
        {
            var identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new(identity);

            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public bool IsDevBuild => CurrentVersion.Major <= 1;

    public bool IsSupportedOS(Version currentVersion) => currentVersion.CompareTo(new Version(10, 0, 19041, 0)) > 0;
}
