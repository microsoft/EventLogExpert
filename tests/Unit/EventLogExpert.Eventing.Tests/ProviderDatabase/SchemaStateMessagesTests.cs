// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.ProviderDatabase;

namespace EventLogExpert.Eventing.Tests.ProviderDatabase;

public sealed class SchemaStateMessagesTests
{
    [Fact]
    public void UnrecognizedSchema_FormatsLabelAndPath()
    {
        // Arrange + Act
        var message = SchemaStateMessages.UnrecognizedSchema("Source database", @"C:\test\providers.db");

        // Assert
        Assert.Equal(
            @"Source database 'C:\test\providers.db' has an unrecognized schema. The file may be corrupt or from a newer or incompatible version of EventLogExpert. Delete or replace the file.",
            message);
    }

    [Fact]
    public void UnsupportedV1OrV2Schema_FormatsPathAndVersion()
    {
        // Arrange + Act
        var message = SchemaStateMessages.UnsupportedV1OrV2Schema(@"C:\test\providers.db", 2);

        // Assert
        Assert.Equal(
            @"Database 'C:\test\providers.db' is at schema v2; this version is no longer supported. Upgrade through an older EventLogExpert release that supports v3 first, or delete the file.",
            message);
    }
}
