// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace EventLogExpert.Logging.Concurrency;

/// <summary>
///     Named cross-process mutex for serializing same-user (medium integrity) work across concurrent EventLogExpert
///     instances. The name is derived from a scope tag plus the canonical target path so equivalent paths coordinate and
///     unrelated ones do not. Cross-integrity-level coordination (the elevated helper) is NOT covered here - use
///     <see cref="InterProcessFileLock" /> for that. The mutex is created with a permissive DACL so a concurrently running
///     prior build (which opens the name requesting full access) still coordinates on the same object.
/// </summary>
public sealed class InterProcessMutex : IDisposable
{
    private readonly Mutex? _mutex;
    private readonly string _name;

    public InterProcessMutex(string scope, string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(scope);
        ArgumentException.ThrowIfNullOrEmpty(path);

        _name = DeriveName(scope, path);
        _mutex = TryCreate(_name);
    }

    public void Dispose() => _mutex?.Dispose();

    public void Run(TimeSpan timeout, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_mutex is null)
        {
            throw new InvalidOperationException(
                $"Interprocess mutex '{_name}' is unavailable; refusing to run guarded work unsynchronized.");
        }

        if (!TryRun(timeout, action))
        {
            throw new TimeoutException(
                $"Timed out after {timeout.TotalSeconds:F0}s acquiring interprocess mutex '{_name}'.");
        }
    }

    public bool TryRun(TimeSpan timeout, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_mutex is null) { return false; }

        bool acquired;

        try
        {
            acquired = _mutex.WaitOne(timeout);
        }
        catch (AbandonedMutexException)
        {
            // Prior owner crashed without releasing; ownership transfers to us and we proceed.
            acquired = true;
        }

        if (!acquired) { return false; }

        try
        {
            action();
        }
        finally
        {
            _mutex.ReleaseMutex();
        }

        return true;
    }

    internal static string DeriveName(string scope, string path)
    {
        // Canonicalize so equivalent paths (relative, separator drift) hash identically. Byte-for-byte compatible with
        // the pre-refactor FileLogSink name when scope == "DebugLog" so a running prior build coordinates on the same name.
        var canonical = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var bytes = Encoding.UTF8.GetBytes(canonical.ToUpperInvariant());
        var hash = SHA256.HashData(bytes);

        return $"Local\\EventLogExpert.{scope}.{Convert.ToHexString(hash, 0, 8)}";
    }

    private static Mutex? TryCreate(string name)
    {
        try
        {
            var security = new MutexSecurity();
            var authenticatedUsers = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);

            // FullControl (not just Synchronize|Modify) so an opener requesting MUTEX_ALL_ACCESS - e.g. a concurrently
            // running prior build that still uses new Mutex(name) - is not denied by this DACL.
            security.AddAccessRule(new MutexAccessRule(
                authenticatedUsers,
                MutexRights.FullControl,
                AccessControlType.Allow));

            return MutexAcl.Create(initiallyOwned: false, name, out _, security);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException)
        {
            return null;
        }
    }
}
