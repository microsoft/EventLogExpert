// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Common.Identity;

public interface IWindowsIdentityProvider
{
    bool IsUserInAdministratorRole();
}
