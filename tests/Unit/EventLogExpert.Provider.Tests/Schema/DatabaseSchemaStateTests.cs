// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Provider.Schema;

namespace EventLogExpert.Provider.Tests.Schema;

public sealed class CatalogSchemaStateTests
{
    [Fact]
    public void NeedsUpgrade_WhenVersionAboveCurrent_ReturnsFalse()
    {
        // Arrange
        var state = new DatabaseSchemaState(DatabaseSchemaVersion.Current + 1);

        // Act + Assert
        Assert.False(state.NeedsUpgrade);
    }

    [Theory]
    [InlineData(DatabaseSchemaVersion.Unknown)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void NeedsUpgrade_WhenVersionBelowCurrent_ReturnsTrue(int version)
    {
        // Arrange
        var state = new DatabaseSchemaState(version);

        // Act + Assert
        Assert.True(state.NeedsUpgrade);
    }

    [Fact]
    public void NeedsUpgrade_WhenVersionEqualsCurrent_ReturnsFalse()
    {
        // Arrange
        var state = new DatabaseSchemaState(DatabaseSchemaVersion.Current);

        // Act + Assert
        Assert.False(state.NeedsUpgrade);
    }
}
