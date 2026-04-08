// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Options;

/// <summary>Options for package deployment operations.</summary>
/// <param name="ForceUpdateFromAnyVersion">Allows updating from any version of the package, even if it would be a downgrade.</param>
/// <param name="ForceTargetAppShutdown">Forces the target application to shut down before the update is applied.</param>
/// <param name="DeferRegistrationWhenPackagesAreInUse">Defers the package registration until the application is no longer in use.</param>
public sealed record PackageDeploymentOptions(
    bool ForceUpdateFromAnyVersion = false,
    bool ForceTargetAppShutdown = false,
    bool DeferRegistrationWhenPackagesAreInUse = false);
