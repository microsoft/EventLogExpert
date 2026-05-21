// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.ProviderDatabase;

namespace EventLogExpert.Eventing.Tests.ProviderDatabase;

public sealed class DatabaseUpgradeExceptionTests
{
    [Fact]
    public void Constructor_SetsPropertiesAndFormatsMessage()
    {
        // Arrange + Act
        var ex = new DatabaseUpgradeException(@"C:\test\providers.db", "schema unrecognized");

        // Assert
        Assert.Equal(@"C:\test\providers.db", ex.DatabasePath);
        Assert.Equal("schema unrecognized", ex.Reason);
        Assert.Equal(@"Database upgrade failed for 'C:\test\providers.db': schema unrecognized", ex.Message);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public void Constructor_WithInnerException_SetsAllProperties()
    {
        // Arrange
        var inner = new InvalidOperationException("inner");

        // Act
        var ex = new DatabaseUpgradeException(@"C:\test\providers.db", "merge failed", inner);

        // Assert
        Assert.Equal(@"C:\test\providers.db", ex.DatabasePath);
        Assert.Equal("merge failed", ex.Reason);
        Assert.Equal(@"Database upgrade failed for 'C:\test\providers.db': merge failed", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }
}
