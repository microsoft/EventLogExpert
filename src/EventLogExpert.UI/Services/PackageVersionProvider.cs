// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;
using Windows.ApplicationModel;

namespace EventLogExpert.UI.Services;

public sealed class PackageVersionProvider : IPackageVersionProvider
{
    public Version GetPackageVersion()
    {
        PackageVersion packageVersion = Package.Current.Id.Version;
        return new($"{packageVersion.Major}.{packageVersion.Minor}.{packageVersion.Build}.{packageVersion.Revision}");
    }
}
