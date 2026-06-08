// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Ipc;
using System.Security.Principal;
using Windows.ApplicationModel;

namespace EventLogExpert.ElevationHelper.Diagnostics;

/// <summary>
///     Environment-probe collector. Returns a <see cref="ProbeEnvelope" /> describing facts the medium-IL parent
///     process cannot directly observe: this elevated process's path, integrity level, and whether packaged-app identity
///     is present. Used to verify that the helper actually launches AND behaves correctly when elevated from inside the
///     MSIX install.
/// </summary>
/// <remarks>
///     The probe deliberately skips the local-provider enumeration check - that requires either the custom
///     <c>EventLogExpert.Eventing.Readers.EventLogSession</c> (which transitively pulls Eventing/Provider into the helper)
///     or a direct EvtOpenSession P/Invoke. Either path is more risk than value at smoke-test time; the integrity-level +
///     package-identity facts are the meaningful diagnostics.
/// </remarks>
internal static class ProbeMode
{
    public static ProbeEnvelope Capture()
    {
        var processPath = Environment.ProcessPath ?? "(null)";
        var integrityLevel = TryGetIntegrityLevel();

        var (packageIdentityOk, packageIdentityError) = TryGetPackageIdentity();

        return new ProbeEnvelope(
            ProcessPath: processPath,
            IntegrityLevel: integrityLevel,
            PackageIdentityOk: packageIdentityOk,
            PackageIdentityError: packageIdentityError,
            LocalProviderEnumerationOk: false,
            LocalProviderEnumerationError: "Skipped in Phase 0 - see ProbeMode remarks.",
            LocalProviderCount: 0);
    }

    private static string TryGetIntegrityLevel()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);

            return principal.IsInRole(WindowsBuiltInRole.Administrator) ? "high" : "medium";
        }
        catch
        {
            return "unknown";
        }
    }

    private static (bool Ok, string? Error) TryGetPackageIdentity()
    {
        try
        {
            var fullName = Package.Current.Id.FullName;

            return (!string.IsNullOrEmpty(fullName), null);
        }
        catch (Exception ex)
        {
            return (false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}

