// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;
using System.Security.Principal;

namespace EventLogExpert.UI.Services;

public sealed class WindowsIdentityProvider : IWindowsIdentityProvider
{
    public bool IsUserInAdministratorRole()
    {
        var identity = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
