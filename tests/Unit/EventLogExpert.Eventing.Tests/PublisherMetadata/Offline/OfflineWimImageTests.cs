// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Interop;
using EventLogExpert.Eventing.PublisherMetadata.Offline;
using EventLogExpert.Logging.Abstractions;

namespace EventLogExpert.Eventing.Tests.PublisherMetadata.Offline;

/// <summary>
///     Drives the WIM read/validate/extract/cancel state machine deterministically through a fake
///     <see cref="IWimNativeApi" />. The real <c>WIMApplyImage</c> path needs administrator privileges and a multi-GB
///     image, so these tests assert the orchestration (index validation, elevation gate, disk precheck, failure-safe temp
///     cleanup, status mapping) while a fake stands in for the native calls and creates/deletes a REAL temp directory so
///     the cleanup assertions are real, not mocked. The actual native apply is covered by the manual real-WIM E2E.
/// </summary>
public sealed class OfflineWimImageTests
{
    [Fact]
    public void ReadIndexList_WhenFileExists_ForwardsToNativeApi()
    {
        using var workspace = new TempWorkspace();
        var nativeApi = new FakeWimNativeApi
        {
            Images = [new WimImageEntry(1, "Image", "Edition", 123)]
        };

        WimImageList result = OfflineWimImage.ReadIndexList(workspace.WimPath, nativeApi, logger: null);

        Assert.Equal(WimImageListStatus.Ok, result.Status);
        Assert.Equal(1, nativeApi.ReadImageListCallCount);
        Assert.Single(result.Images);
    }

    [Fact]
    public void ReadIndexList_WhenFileMissing_ReturnsNotAWimWithoutCallingNative()
    {
        var nativeApi = new FakeWimNativeApi();

        WimImageList result = OfflineWimImage.ReadIndexList(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".wim"), nativeApi, logger: null);

        Assert.Equal(WimImageListStatus.NotAWim, result.Status);
        Assert.Equal(0, nativeApi.ReadImageListCallCount);
    }

    [Fact]
    public async Task TryExtractAsync_DisposeIsIdempotent()
    {
        using var workspace = new TempWorkspace();
        var nativeApi = new FakeWimNativeApi { OnApply = WriteAPartialFile };

        OfflineWimExtractResult result = await OfflineWimImage.TryExtractAsync(
            workspace.WimPath, 1, workspace.TempParent, nativeApi, logger: null, CancellationToken.None);

        result.Image!.Dispose();
        result.Image.Dispose();

        Assert.False(Directory.Exists(result.Image.ExtractedRoot));
    }

    [Fact]
    public async Task TryExtractAsync_WhenAlreadyCancelled_ReturnsCancelledWithoutApplying()
    {
        using var workspace = new TempWorkspace();
        var nativeApi = new FakeWimNativeApi();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        OfflineWimExtractResult result = await OfflineWimImage.TryExtractAsync(
            workspace.WimPath, 1, workspace.TempParent, nativeApi, logger: null, cts.Token);

        Assert.Equal(OfflineWimExtractStatus.Cancelled, result.Status);
        Assert.Equal(0, nativeApi.ApplyCallCount);
    }

    [Fact]
    public async Task TryExtractAsync_WhenApplyAborts_ReturnsCancelledAndDeletesTheTemp()
    {
        using var workspace = new TempWorkspace();
        var nativeApi = new FakeWimNativeApi
        {
            ApplyResult = Win32ErrorCodes.ERROR_REQUEST_ABORTED,
            OnApply = WriteAPartialFile
        };

        OfflineWimExtractResult result = await OfflineWimImage.TryExtractAsync(
            workspace.WimPath, 1, workspace.TempParent, nativeApi, logger: null, CancellationToken.None);

        Assert.Equal(OfflineWimExtractStatus.Cancelled, result.Status);
        Assert.Empty(Directory.GetDirectories(workspace.TempParent));
    }

    [Fact]
    public async Task TryExtractAsync_WhenApplyFails_ReturnsApplyFailedAndDeletesTheTemp()
    {
        using var workspace = new TempWorkspace();
        var nativeApi = new FakeWimNativeApi
        {
            ApplyResult = Win32ErrorCodes.ERROR_INVALID_DATA,
            OnApply = WriteAPartialFile
        };

        OfflineWimExtractResult result = await OfflineWimImage.TryExtractAsync(
            workspace.WimPath, 1, workspace.TempParent, nativeApi, logger: null, CancellationToken.None);

        Assert.Equal(OfflineWimExtractStatus.ApplyFailed, result.Status);
        Assert.Equal(1, nativeApi.ApplyCallCount);
        // The partial extraction must not leak: no ELX_WIM_* directory remains under the temp parent.
        Assert.Empty(Directory.GetDirectories(workspace.TempParent));
    }

    [Fact]
    public async Task TryExtractAsync_WhenApplySucceeds_ReturnsExtractedRootThatDisposeDeletes()
    {
        using var workspace = new TempWorkspace();
        var nativeApi = new FakeWimNativeApi { OnApply = WriteAPartialFile };

        OfflineWimExtractResult result = await OfflineWimImage.TryExtractAsync(
            workspace.WimPath, 1, workspace.TempParent, nativeApi, logger: null, CancellationToken.None);

        Assert.Equal(OfflineWimExtractStatus.Extracted, result.Status);
        Assert.NotNull(result.Image);
        Assert.True(Directory.Exists(result.Image!.ExtractedRoot));
        Assert.True(File.Exists(Path.Combine(result.Image.ExtractedRoot, "applied.txt")));

        result.Image.Dispose();

        Assert.False(Directory.Exists(result.Image.ExtractedRoot));
    }

    [Fact]
    public async Task TryExtractAsync_WhenExtractionContainsReadOnlyFiles_StillDeletesTheTemp()
    {
        using var workspace = new TempWorkspace();
        var nativeApi = new FakeWimNativeApi
        {
            ApplyResult = Win32ErrorCodes.ERROR_INVALID_DATA,
            OnApply = WriteAReadOnlyFile
        };

        OfflineWimExtractResult result = await OfflineWimImage.TryExtractAsync(
            workspace.WimPath, 1, workspace.TempParent, nativeApi, logger: null, CancellationToken.None);

        Assert.Equal(OfflineWimExtractStatus.ApplyFailed, result.Status);
        // WIMApplyImage restores read-only attributes; cleanup must clear them or Directory.Delete would throw.
        Assert.Empty(Directory.GetDirectories(workspace.TempParent));
    }

    [Fact]
    public async Task TryExtractAsync_WhenImageLargerThanFreeSpace_ReturnsInsufficientSpaceWithoutApplying()
    {
        using var workspace = new TempWorkspace();
        var nativeApi = new FakeWimNativeApi
        {
            Images = [new WimImageEntry(1, "Huge", "Edition", long.MaxValue)]
        };

        OfflineWimExtractResult result = await OfflineWimImage.TryExtractAsync(
            workspace.WimPath, 1, workspace.TempParent, nativeApi, logger: null, CancellationToken.None);

        Assert.Equal(OfflineWimExtractStatus.InsufficientSpace, result.Status);
        Assert.Equal(0, nativeApi.ApplyCallCount);
    }

    [Fact]
    public async Task TryExtractAsync_WhenIndexNotInImage_ReturnsIndexOutOfRangeWithoutApplying()
    {
        using var workspace = new TempWorkspace();
        var nativeApi = new FakeWimNativeApi
        {
            Images = [new WimImageEntry(1, "Only image", "Edition", null)]
        };

        OfflineWimExtractResult result = await OfflineWimImage.TryExtractAsync(
            workspace.WimPath, 5, workspace.TempParent, nativeApi, logger: null, CancellationToken.None);

        Assert.Equal(OfflineWimExtractStatus.IndexOutOfRange, result.Status);
        Assert.Equal(0, nativeApi.ApplyCallCount);
    }

    [Fact]
    public async Task TryExtractAsync_WhenNotAWim_ReturnsNotAWimWithoutApplying()
    {
        using var workspace = new TempWorkspace();
        var nativeApi = new FakeWimNativeApi { ImageListStatus = WimImageListStatus.NotAWim };

        OfflineWimExtractResult result = await OfflineWimImage.TryExtractAsync(
            workspace.WimPath, 1, workspace.TempParent, nativeApi, logger: null, CancellationToken.None);

        Assert.Equal(OfflineWimExtractStatus.NotAWim, result.Status);
        Assert.Equal(0, nativeApi.ApplyCallCount);
    }

    [Fact]
    public async Task TryExtractAsync_WhenNotElevated_ReturnsNeedsElevationWithoutApplying()
    {
        using var workspace = new TempWorkspace();
        var nativeApi = new FakeWimNativeApi { Elevated = false };

        OfflineWimExtractResult result = await OfflineWimImage.TryExtractAsync(
            workspace.WimPath, 1, workspace.TempParent, nativeApi, logger: null, CancellationToken.None);

        Assert.Equal(OfflineWimExtractStatus.NeedsElevation, result.Status);
        Assert.Null(result.Image);
        Assert.Equal(0, nativeApi.ApplyCallCount);
        Assert.Empty(Directory.GetDirectories(workspace.TempParent));
    }

    private static void WriteAPartialFile(string destinationDirectory) =>
        File.WriteAllText(Path.Combine(destinationDirectory, "applied.txt"), "extracted");

    private static void WriteAReadOnlyFile(string destinationDirectory)
    {
        string path = Path.Combine(destinationDirectory, "readonly.bin");
        File.WriteAllText(path, "locked");
        File.SetAttributes(path, FileAttributes.ReadOnly);
    }

    private sealed class FakeWimNativeApi : IWimNativeApi
    {
        public int ApplyCallCount { get; private set; }

        public int ApplyResult { get; init; } = Win32ErrorCodes.ERROR_SUCCESS;

        public bool Elevated { get; init; } = true;

        public WimImageListStatus ImageListStatus { get; init; } = WimImageListStatus.Ok;

        public IReadOnlyList<WimImageEntry> Images { get; init; } = [new WimImageEntry(1, "Image", "Edition", null)];

        public Action<string>? OnApply { get; init; }

        public int ReadImageListCallCount { get; private set; }

        public int ApplyImage(
            string wimPath, int imageIndex, string destinationDirectory, string scratchDirectory, CancellationToken cancellationToken, ITraceLogger? logger)
        {
            ApplyCallCount++;
            OnApply?.Invoke(destinationDirectory);

            return ApplyResult;
        }

        public bool IsProcessElevated() => Elevated;

        public WimImageList ReadImageList(string wimPath, ITraceLogger? logger)
        {
            ReadImageListCallCount++;

            return ImageListStatus == WimImageListStatus.Ok
                ? new WimImageList(WimImageListStatus.Ok, Images)
                : WimImageList.NotAWim;
        }
    }

    private sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace()
        {
            TempParent = Path.Combine(Path.GetTempPath(), "elx_wimtest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(TempParent);
            WimPath = Path.Combine(TempParent, "image.wim");
            File.WriteAllText(WimPath, "not a real wim - the fake ignores the content");
        }

        public string TempParent { get; }

        public string WimPath { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(TempParent)) { Directory.Delete(TempParent, recursive: true); }
            }
            catch (IOException)
            {
                // Best-effort cleanup of the test workspace.
            }
        }
    }
}
