// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.PublisherMetadata.Offline;

namespace EventLogExpert.Eventing.Tests.PublisherMetadata.Offline;

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
