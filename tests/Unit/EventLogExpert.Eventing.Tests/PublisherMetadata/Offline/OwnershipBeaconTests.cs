// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.PublisherMetadata.Offline;

namespace EventLogExpert.Eventing.Tests.PublisherMetadata.Offline;

/// <summary>
///     Covers the machine-global liveness beacon used to distinguish a live offline-image scratch resource (hive
///     mount or WIM extraction) from one orphaned by a crashed/self-terminated run. A held beacon reads as alive; once
///     released (or never created) it reads as dead so the next run's reconciliation reclaims the resource.
/// </summary>
public sealed class OwnershipBeaconTests
{
    [Fact]
    public void IsAlive_WhenNoBeaconExists_ReturnsFalse()
    {
        Assert.False(OwnershipBeacon.IsAlive("ELX_TEST_" + Guid.NewGuid().ToString("N")));
    }

    [Fact]
    public void IsAlive_WhileBeaconHeld_ReturnsTrue_AndFalseAfterRelease()
    {
        string name = "ELX_TEST_" + Guid.NewGuid().ToString("N");

        Mutex? beacon = OwnershipBeacon.TryCreate(name, logger: null);

        Assert.NotNull(beacon);
        Assert.True(OwnershipBeacon.IsAlive(name));

        beacon!.Dispose();

        Assert.False(OwnershipBeacon.IsAlive(name));
    }
}
