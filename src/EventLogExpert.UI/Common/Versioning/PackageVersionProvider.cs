// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Windows.ApplicationModel;

namespace EventLogExpert.UI.Common.Versioning;

internal sealed class PackageVersionProvider : IPackageVersionProvider
{
    public Version GetPackageVersion()
    {
        PackageVersion packageVersion = Package.Current.Id.Version;
        return new Version($"{packageVersion.Major}.{packageVersion.Minor}.{packageVersion.Build}.{packageVersion.Revision}");
    }
}
