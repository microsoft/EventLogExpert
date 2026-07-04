// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;

namespace EventLogExpert.Eventing.OfflineImaging.Workspace;

internal static class OwnershipBeacon
{
    public static bool IsAlive(string name)
    {
        try
        {
            using Mutex owner = Mutex.OpenExisting($"Local\\{name}");

            return true;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // Unopenable means someone may own it; never reclaim that resource.
            return true;
        }
    }

    public static Mutex? TryCreate(string name, ITraceLogger? logger)
    {
        try
        {
            return new Mutex(initiallyOwned: false, name: $"Local\\{name}");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            logger?.Debug($"{nameof(OwnershipBeacon)}: could not create ownership beacon for {name}: {ex.Message}");

            return null;
        }
    }
}
