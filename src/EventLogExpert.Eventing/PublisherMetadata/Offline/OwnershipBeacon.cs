// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;

namespace EventLogExpert.Eventing.PublisherMetadata.Offline;

/// <summary>
///     A machine-global "liveness beacon" for a scratch resource (a loaded registry hive mount or an extracted WIM
///     folder): an open, unowned named <see cref="Mutex" /> at <c>Global\&lt;name&gt;</c>. While the owning process holds
///     the handle the beacon opens; when the process dies - including a hard <see cref="Environment.Exit" /> or a crash -
///     the OS releases it, so a later run's reconciliation sweep can tell a leftover resource is an orphan (beacon gone)
///     versus in use by a live sibling (beacon still opens). Shared by <see cref="OfflineRegistryHive" /> recovery mounts
///     and <see cref="OfflineWimImage" /> extractions so both reclaim identically.
/// </summary>
internal static class OwnershipBeacon
{
    /// <summary>True when a live owner still holds the beacon for <paramref name="name" /> (so it must not be reclaimed).</summary>
    public static bool IsAlive(string name)
    {
        try
        {
            using Mutex owner = Mutex.OpenExisting($"Global\\{name}");

            return true;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // The beacon exists but is not openable by us; treat as alive (never reclaim someone else's live resource).
            return true;
        }
    }

    /// <summary>
    ///     Publishes a beacon for <paramref name="name" />. Returns the held <see cref="Mutex" /> (dispose to release on
    ///     normal cleanup) or <see langword="null" /> if it could not be created.
    /// </summary>
    public static Mutex? TryCreate(string name, ITraceLogger? logger)
    {
        try
        {
            return new Mutex(initiallyOwned: false, name: $"Global\\{name}");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            logger?.Debug($"{nameof(OwnershipBeacon)}: could not create ownership beacon for {name}: {ex.Message}");

            return null;
        }
    }
}
