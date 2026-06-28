// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.PublisherMetadata.Offline;

namespace EventLogExpert.Eventing.Tests.PublisherMetadata.Offline;

/// <summary>
///     Guards the <c>TOKEN_PRIVILEGES</c> P/Invoke marshalling that the recovery path depends on. A wrong struct pack
///     and a privilege the token does not hold BOTH make <c>AdjustTokenPrivileges</c> report <c>ERROR_NOT_ALL_ASSIGNED</c>
///     , so only asserting SUCCESS on a privilege EVERY token holds proves the marshalling is correct - no admin required.
/// </summary>
public sealed class BackupRestorePrivilegeScopeTests
{
    [Fact]
    public void CanEnablePrivilege_ForAnUnknownPrivilegeName_ReturnsFalse()
    {
        Assert.False(BackupRestorePrivilegeScope.CanEnablePrivilegeForTest("SeThisIsNotARealPrivilege"));
    }

    [Fact]
    public void CanEnablePrivilege_ForAPrivilegeEveryTokenHolds_Succeeds()
    {
        // SeChangeNotifyPrivilege (bypass traverse checking) is present and enabled in every process token. A SUCCESS
        // here proves the LUID landed at the right struct offset; a Pack regression would report NOT_ALL_ASSIGNED.
        Assert.True(BackupRestorePrivilegeScope.CanEnablePrivilegeForTest("SeChangeNotifyPrivilege"));
    }

    [Fact]
    public void CanEnablePrivilege_ForAPrivilegeTheTokenDoesNotHold_ReturnsFalse()
    {
        // SeCreateTokenPrivilege is held by virtually no token (not even elevated admins), so enabling it must fail the
        // success predicate - confirming the predicate rejects NOT_ALL_ASSIGNED rather than trusting the BOOL return.
        Assert.False(BackupRestorePrivilegeScope.CanEnablePrivilegeForTest("SeCreateTokenPrivilege"));
    }
}
