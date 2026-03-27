// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;

namespace EventLogExpert.UI.Services;

public interface ICurrentVersionProvider
{
    Version CurrentVersion { get; }

    bool IsAdmin { get; }

    bool IsDevBuild { get; }

    bool IsSupportedOS(Version currentVersion);
}

public sealed class CurrentVersionProvider(
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
