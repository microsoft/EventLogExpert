// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Windows.ApplicationModel;

namespace EventLogExpert.UI.Services;

public interface ICurrentVersionProvider
{
    Version CurrentVersion { get; }

    bool IsDevBuild { get; }
}

public class CurrentVersionProvider : ICurrentVersionProvider
{
    public CurrentVersionProvider()
    {
        PackageVersion packageVersion = Package.Current.Id.Version;
        CurrentVersion = new($"{packageVersion.Major}.{packageVersion.Minor}.{packageVersion.Build}.{packageVersion.Revision}");
    }

    public Version CurrentVersion { get; init; }

    public bool IsDevBuild => CurrentVersion.Major <= 1;
}
