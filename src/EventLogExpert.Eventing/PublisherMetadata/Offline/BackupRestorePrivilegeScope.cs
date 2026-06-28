// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Interop;
using EventLogExpert.Logging.Abstractions;
using System.Runtime.InteropServices;
using static EventLogExpert.Eventing.Interop.NativeMethods;

namespace EventLogExpert.Eventing.PublisherMetadata.Offline;

/// <summary>
///     A short-lived, process-wide-serialized enabling of <c>SeBackupPrivilege</c> + <c>SeRestorePrivilege</c> for
///     the duration of a single privileged registry call (<c>RegLoadKey</c> / <c>RegUnLoadKey</c>). The privileges are
///     present-but-disabled in an elevated token; this enables them, runs the call, then restores each privilege to its
///     EXACT prior state - never leaving backup/restore (ACL-bypass) semantics enabled for unrelated code in a long-lived
///     host such as the MAUI app. A static gate serializes the enable/call/revert sections so two concurrent offline loads
///     cannot disable a privilege the other is mid-call relying on.
/// </summary>
internal sealed class BackupRestorePrivilegeScope : IDisposable
{
    private static readonly Lock s_gate = new();
    // SeRestorePrivilege is required by both RegLoadKey and RegUnLoadKey; SeBackupPrivilege by RegLoadKey. Enable both.
    private static readonly string[] s_requiredPrivileges = ["SeBackupPrivilege", "SeRestorePrivilege"];

    private readonly List<TOKEN_PRIVILEGES> _statesToRestore;
    private readonly IntPtr _token;

    private bool _disposed;

    private BackupRestorePrivilegeScope(IntPtr token, List<TOKEN_PRIVILEGES> statesToRestore)
    {
        _token = token;
        _statesToRestore = statesToRestore;
    }

    public void Dispose()
    {
        if (_disposed) { return; }

        _disposed = true;

        try
        {
            RestoreAll(_token, _statesToRestore);
            CloseHandle(_token);
        }
        finally
        {
            s_gate.Exit();
        }
    }

    /// <summary>
    ///     Verifies the token holds <paramref name="privilegeName" /> by enabling then immediately restoring it. Exists
    ///     so a CI unit test can assert the <c>Pack=4</c> marshalling against a privilege EVERY token holds (
    ///     <c>SeChangeNotifyPrivilege</c>) - a wrong struct pack and a not-held privilege both report
    ///     <c>ERROR_NOT_ALL_ASSIGNED</c>, so only a SUCCESS on a held privilege proves the marshalling is correct.
    /// </summary>
    internal static bool CanEnablePrivilegeForTest(string privilegeName)
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr token))
        {
            return false;
        }

        try
        {
            if (!TryEnable(token, privilegeName, out TOKEN_PRIVILEGES previous)) { return false; }

            var state = previous;
            AdjustTokenPrivileges(token, false, ref state, Marshal.SizeOf<TOKEN_PRIVILEGES>(), ref previous, out _);

            return true;
        }
        finally
        {
            CloseHandle(token);
        }
    }

    /// <summary>
    ///     Enters the gate and enables both privileges, capturing each prior state for restore. Returns
    ///     <see langword="null" /> (gate released, nothing changed) when the token does not hold them - i.e. the process is
    ///     not elevated, which the caller surfaces as "needs administrator". The returned scope MUST be disposed to revert +
    ///     release the gate.
    /// </summary>
    internal static BackupRestorePrivilegeScope? TryAcquire(ITraceLogger? logger)
    {
        s_gate.Enter();

        IntPtr token = IntPtr.Zero;
        var restore = new List<TOKEN_PRIVILEGES>();

        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out token))
            {
                logger?.Debug($"{nameof(BackupRestorePrivilegeScope)}: OpenProcessToken failed (error {Marshal.GetLastWin32Error()}).");

                return Release(token, restore);
            }

            foreach (string privilege in s_requiredPrivileges)
            {
                if (!TryEnable(token, privilege, out TOKEN_PRIVILEGES previous))
                {
                    logger?.Debug($"{nameof(BackupRestorePrivilegeScope)}: token does not hold {privilege}; not elevated.");

                    // Atomic rollback: restore any privilege already enabled before returning not-held.
                    RestoreAll(token, restore);

                    return Release(token, restore);
                }

                restore.Add(previous);
            }

            return new BackupRestorePrivilegeScope(token, restore);
        }
        catch
        {
            RestoreAll(token, restore);
            Release(token, restore);

            throw;
        }
    }

    private static BackupRestorePrivilegeScope? Release(IntPtr token, List<TOKEN_PRIVILEGES> _)
    {
        if (token != IntPtr.Zero) { CloseHandle(token); }

        s_gate.Exit();

        return null;
    }

    private static void RestoreAll(IntPtr token, List<TOKEN_PRIVILEGES> states)
    {
        if (token == IntPtr.Zero) { return; }

        foreach (TOKEN_PRIVILEGES previous in states)
        {
            var state = previous;
            var discard = new TOKEN_PRIVILEGES();
            AdjustTokenPrivileges(token, false, ref state, Marshal.SizeOf<TOKEN_PRIVILEGES>(), ref discard, out _);
        }
    }

    // Enables a single privilege; true iff the token HOLDS it and it is now enabled. AdjustTokenPrivileges returns TRUE
    // even when the privilege is not held (it then sets ERROR_NOT_ALL_ASSIGNED), so the SUCCESS check is mandatory.
    private static bool TryEnable(IntPtr token, string privilegeName, out TOKEN_PRIVILEGES previous)
    {
        previous = default;

        if (!LookupPrivilegeValue(null, privilegeName, out long luid)) { return false; }

        var newState = new TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = SE_PRIVILEGE_ENABLED };
        var capturedPrevious = new TOKEN_PRIVILEGES();
        bool adjusted = AdjustTokenPrivileges(token, false, ref newState, Marshal.SizeOf<TOKEN_PRIVILEGES>(), ref capturedPrevious, out _);

        if (adjusted && Marshal.GetLastWin32Error() == Win32ErrorCodes.ERROR_SUCCESS)
        {
            previous = capturedPrevious;

            return true;
        }

        return false;
    }
}
