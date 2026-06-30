// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.PublisherMetadata.Offline;

namespace EventLogExpert.Eventing.Tests.PublisherMetadata.Offline;

public sealed class OfflineExtractionReconciliationTests
{
    [Fact]
    public void ReconcileOrphanedExtractions_IgnoresNonExtractionDirectories()
    {
        using var scratch = new TempScratch();
        string unrelated = Path.Combine(scratch.Root, "ELX_HIVE_keepme");
        Directory.CreateDirectory(unrelated);

        OfflineWimImage.ReconcileOrphanedExtractions(scratch.Root, logger: null);

        Assert.True(Directory.Exists(unrelated));
    }

    [Fact]
    public void ReconcileOrphanedExtractions_WhenBeaconDead_DeletesExtraction()
    {
        using var scratch = new TempScratch();
        string orphan = Path.Combine(scratch.Root, "ELX_WIM_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(orphan);
        File.WriteAllText(Path.Combine(orphan, "leaf.txt"), "x");

        OfflineWimImage.ReconcileOrphanedExtractions(scratch.Root, logger: null);

        Assert.False(Directory.Exists(orphan));
    }

    [Fact]
    public void ReconcileOrphanedExtractions_WhenBeaconLive_KeepsExtraction()
    {
        using var scratch = new TempScratch();
        string name = "ELX_WIM_" + Guid.NewGuid().ToString("N");
        string live = Path.Combine(scratch.Root, name);
        Directory.CreateDirectory(live);

        using Mutex? beacon = OwnershipBeacon.TryCreate(name, logger: null);
        Assert.NotNull(beacon);

        OfflineWimImage.ReconcileOrphanedExtractions(scratch.Root, logger: null);

        Assert.True(Directory.Exists(live));
    }

    private sealed class TempScratch : IDisposable
    {
        public TempScratch()
        {
            Root = Path.Combine(Path.GetTempPath(), "ELX_SCRATCH_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root)) { Directory.Delete(Root, recursive: true); }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
