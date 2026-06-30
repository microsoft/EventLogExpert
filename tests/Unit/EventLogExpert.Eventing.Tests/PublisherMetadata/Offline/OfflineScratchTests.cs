// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.PublisherMetadata.Offline;

namespace EventLogExpert.Eventing.Tests.PublisherMetadata.Offline;

/// <summary>
///     Covers the scratch-root write probe used as a fast pre-flight before the long native WIM apply: a writable
///     folder passes, a non-writable target returns an actionable message, and a missing folder is created. The real
///     Controlled Folder Access denial is environment-specific, so the "blocked" case is simulated by pointing the probe
///     at a path that is actually a file (forcing the same failure-to-write the probe must report rather than throw).
/// </summary>
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
