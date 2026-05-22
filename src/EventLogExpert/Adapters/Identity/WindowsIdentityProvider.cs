// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Common.Identity;
using System.Security.Principal;

namespace EventLogExpert.Adapters.Identity;

internal sealed class WindowsIdentityProvider : IWindowsIdentityProvider
{
    public bool IsUserInAdministratorRole()
    {
        var identity = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
