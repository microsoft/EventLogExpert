// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Windows.ApplicationModel;

namespace EventLogExpert.UI.Services;

public interface ICurrentVersionProvider
{
    Version CurrentVersion { get; }

    bool IsDevBuild { get; }

    bool IsSupportedOS(Version currentVersion);
}

public sealed class CurrentVersionProvider : ICurrentVersionProvider
{
    public CurrentVersionProvider()
    {
        PackageVersion packageVersion = Package.Current.Id.Version;
        CurrentVersion = new Version($"{packageVersion.Major}.{packageVersion.Minor}.{packageVersion.Build}.{packageVersion.Revision}");
    }

    public Version CurrentVersion { get; init; }

    public bool IsDevBuild => CurrentVersion.Major <= 1;

    public bool IsSupportedOS(Version currentVersion) => currentVersion.CompareTo(new Version(10, 0, 19041, 0)) > 0;
}
