// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Interfaces;

public interface IWindowsIdentityProvider
{
    bool IsUserInAdministratorRole();
}
