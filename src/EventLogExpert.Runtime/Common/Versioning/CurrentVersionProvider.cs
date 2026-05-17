// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Common.Identity;

namespace EventLogExpert.Runtime.Common.Versioning;

internal sealed class CurrentVersionProvider(
    IPackageVersionProvider packageVersionProvider,
    IWindowsIdentityProvider identityProvider) : ICurrentVersionProvider
{
    private readonly IWindowsIdentityProvider _identityProvider = identityProvider;
    private readonly IPackageVersionProvider _packageVersionProvider = packageVersionProvider;

    public Version CurrentVersion => _packageVersionProvider.GetPackageVersion();

    public bool IsAdmin => _identityProvider.IsUserInAdministratorRole();

    public bool IsDevBuild => CurrentVersion.Major <= 1;

    public bool IsSupportedOS(Version currentVersion) => currentVersion.CompareTo(new Version(10, 0, 19041, 0)) > 0;
}
