// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.ProviderDatabase;

namespace EventLogExpert.Eventing.Tests.ProviderDatabase;

public sealed class ProviderDatabaseSchemaStateTests
{
    [Theory]
    [InlineData(ProviderDatabaseSchemaVersion.Unknown)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void NeedsUpgrade_WhenVersionBelowCurrent_ReturnsTrue(int version)
    {
        // Arrange
        var state = new ProviderDatabaseSchemaState(version);

        // Act + Assert
        Assert.True(state.NeedsUpgrade);
    }

    [Fact]
    public void NeedsUpgrade_WhenVersionEqualsCurrent_ReturnsFalse()
    {
        // Arrange
        var state = new ProviderDatabaseSchemaState(ProviderDatabaseSchemaVersion.Current);

        // Act + Assert
        Assert.False(state.NeedsUpgrade);
    }

    [Fact]
    public void NeedsUpgrade_WhenVersionAboveCurrent_ReturnsFalse()
    {
        // Arrange
        var state = new ProviderDatabaseSchemaState(ProviderDatabaseSchemaVersion.Current + 1);

        // Act + Assert
        Assert.False(state.NeedsUpgrade);
    }
}
