// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Security.AccessControl;
using System.Security.Principal;

namespace EventLogExpert.Logging.Concurrency;

/// <summary>
///     Cross-process, cross-integrity-level advisory lock backed by a sentinel file held open with
///     <see cref="FileShare.None" />. Unlike a named mutex, file access is governed by the file/directory DACL rather than
///     the Windows mandatory integrity label, so a medium-integrity app instance and the high-integrity elevated helper
///     coordinate on the same lock. The sentinel is created with a permissive DACL and is never deleted (deleting it would
///     open a pending-deletion race); the OS releases the lock automatically if the holder crashes.
/// </summary>
public sealed class InterProcessFileLock
{
    private static readonly TimeSpan s_maxBackoff = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan s_minBackoff = TimeSpan.FromMilliseconds(50);

    private readonly string _lockPath;
    private readonly string _scope;

    public InterProcessFileLock(string scope, string targetPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(scope);
        ArgumentException.ThrowIfNullOrEmpty(targetPath);

        _scope = scope;
        _lockPath = $"{targetPath}.{scope}.lock";
    }

    public void Run(TimeSpan timeout, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (!TryRun(timeout, action))
        {
            throw new TimeoutException(
                $"Timed out after {timeout.TotalSeconds:0.###}s acquiring interprocess file lock '{_lockPath}' (scope '{_scope}').");
        }
    }

    public bool TryRun(TimeSpan timeout, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        var backoff = s_minBackoff;

        while (true)
        {
            FileStream? stream;

            try
            {
                stream = OpenExclusive(_lockPath);
            }
            catch (IOException ex) when (IsContention(ex))
            {
                // Held by another process. Retry with capped exponential backoff + jitter until the deadline.
                if (Environment.TickCount64 >= deadline) { return false; }

                Thread.Sleep(NextBackoff(ref backoff));

                continue;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                // Could not create/open the sentinel at all. If the directory itself is not writable there can be no
                // concurrent writer to coordinate with, so the lock is moot and running unguarded is safe. If the
                // directory IS writable, the sentinel's own DACL denies us (a foreign-principal lock left on disk) -
                // fail loud rather than mutate unsynchronized.
                if (DirectoryAllowsWrite(_lockPath)) { throw; }

                action();

                return true;
            }

            using (stream)
            {
                action();
            }

            return true;
        }
    }

    private static bool DirectoryAllowsWrite(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);

        if (string.IsNullOrEmpty(directory)) { return false; }

        var probePath = Path.Combine(directory, $".{Guid.NewGuid():N}.wtest");

        try
        {
            using var probe = new FileStream(
                probePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);

            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private static bool IsContention(IOException ex)
    {
        const int ErrorSharingViolation = unchecked((int)0x80070020);
        const int ErrorLockViolation = unchecked((int)0x80070021);

        return ex.HResult is ErrorSharingViolation or ErrorLockViolation;
    }

    private static TimeSpan NextBackoff(ref TimeSpan current)
    {
        var jitterMs = Random.Shared.Next(0, (int)current.TotalMilliseconds + 1);
        var wait = TimeSpan.FromMilliseconds(current.TotalMilliseconds + jitterMs);

        var doubled = TimeSpan.FromMilliseconds(current.TotalMilliseconds * 2);
        current = doubled > s_maxBackoff ? s_maxBackoff : doubled;

        return wait > s_maxBackoff ? s_maxBackoff : wait;
    }

    private static FileStream OpenExclusive(string lockPath)
    {
        // Permissive DACL on creation so a different principal (the elevated helper may run under a separate admin
        // account) can still open the never-deleted sentinel. Applied only when the file is created; an existing file
        // keeps its own DACL.
        var security = new FileSecurity();
        var authenticatedUsers = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);

        security.AddAccessRule(new FileSystemAccessRule(
            authenticatedUsers,
            FileSystemRights.FullControl,
            AccessControlType.Allow));

        var lockFile = new FileInfo(lockPath);

        // Request only Modify (read/write/delete) rather than FullControl: holding a FileShare.None handle needs no
        // WRITE_DAC/WRITE_OWNER, and asking for less avoids a false denial when opening a sentinel a different principal
        // created with a narrower DACL.
        return lockFile.Create(
            FileMode.OpenOrCreate,
            FileSystemRights.Modify,
            FileShare.None,
            bufferSize: 1,
            FileOptions.None,
            security);
    }
}
