// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.OfflineImaging.Workspace;

namespace EventLogExpert.Eventing.OfflineImaging.Tests.Workspace;

public sealed class OfflineScratchTests
{
    [Fact]
    public void ProbeWritable_MissingDirectory_IsCreated()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ELX_PROBE_" + Guid.NewGuid().ToString("N"));

        try
        {
            OfflineScratch.ProbeWritable(directory);

            Assert.True(Directory.Exists(directory));
        }
        finally
        {
            if (Directory.Exists(directory)) { Directory.Delete(directory, recursive: true); }
        }
    }

    [Fact]
    public void ProbeWritable_WhenPathIsAFile_ReturnsMessage()
    {
        string filePath = Path.Combine(Path.GetTempPath(), "ELX_PROBE_" + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllText(filePath, "x");

        try
        {
            // CreateDirectory over an existing file collides; the probe must surface a message, not throw.
            string? message = OfflineScratch.ProbeWritable(filePath);

            Assert.NotNull(message);
            Assert.Contains(filePath, message);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void ProbeWritable_WritableDirectory_ReturnsNull()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ELX_PROBE_" + Guid.NewGuid().ToString("N"));

        try
        {
            Assert.Null(OfflineScratch.ProbeWritable(directory));
        }
        finally
        {
            if (Directory.Exists(directory)) { Directory.Delete(directory, recursive: true); }
        }
    }
}
