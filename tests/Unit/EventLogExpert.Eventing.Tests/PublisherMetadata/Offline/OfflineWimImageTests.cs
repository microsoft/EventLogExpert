// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Interop;
using EventLogExpert.Eventing.PublisherMetadata.Offline;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Eventing.Tests.PublisherMetadata.Offline;

public sealed class OfflineWimImageTests
{
    [Fact]
    public void ReadIndexList_WhenFileExists_ForwardsToOperations()
    {
        using var workspace = new TempWorkspace();
        var nativeApi = new FakeWimOperations
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
        var nativeApi = new FakeWimOperations();

        WimImageList result = OfflineWimImage.ReadIndexList(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".wim"), nativeApi, logger: null);

        Assert.Equal(WimImageListStatus.NotAWim, result.Status);
        Assert.Equal(0, nativeApi.ReadImageListCallCount);
    }

    [Fact]
    public async Task TryExtractAsync_DisposeIsIdempotent()
    {
        using var workspace = new TempWorkspace();
        var nativeApi = new FakeWimOperations { OnApply = WriteAPartialFile };

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
        var nativeApi = new FakeWimOperations();
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
        var nativeApi = new FakeWimOperations
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
        var nativeApi = new FakeWimOperations
        {
            ApplyResult = Win32ErrorCodes.ERROR_INVALID_DATA,
            OnApply = WriteAPartialFile
        };

        OfflineWimExtractResult result = await OfflineWimImage.TryExtractAsync(
            workspace.WimPath, 1, workspace.TempParent, nativeApi, logger: null, CancellationToken.None);

        Assert.Equal(OfflineWimExtractStatus.ApplyFailed, result.Status);
        Assert.Equal(1, nativeApi.ApplyCallCount);
        Assert.Empty(Directory.GetDirectories(workspace.TempParent));
    }

    [Fact]
    public async Task TryExtractAsync_WhenApplySucceeds_ReturnsExtractedRootThatDisposeDeletes()
    {
        using var workspace = new TempWorkspace();
        var nativeApi = new FakeWimOperations { OnApply = WriteAPartialFile };

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
    public async Task TryExtractAsync_WhenApplySucceeds_StreamsExtractingBeforeApplyAndExtractedAfter()
    {
        using var workspace = new TempWorkspace();
        var events = new List<string>();
        var logger = new SequenceRecordingTraceLogger(events);
        var nativeApi = new FakeWimOperations { OnApply = WriteAPartialFile, Events = events };

        OfflineWimExtractResult result = await OfflineWimImage.TryExtractAsync(
            workspace.WimPath, 1, workspace.TempParent, nativeApi, logger, CancellationToken.None);

        Assert.Equal(OfflineWimExtractStatus.Extracted, result.Status);
        var relevant = events
            .Where(entry => entry == "apply"
                || entry.StartsWith("Extracting image index", StringComparison.Ordinal)
                || entry.StartsWith("Extracted image index", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(3, relevant.Count);
        Assert.StartsWith("Extracting image index", relevant[0], StringComparison.Ordinal);
        Assert.Equal("apply", relevant[1]);
        Assert.StartsWith("Extracted image index", relevant[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task TryExtractAsync_WhenExtractionContainsReadOnlyFiles_StillDeletesTheTemp()
    {
        using var workspace = new TempWorkspace();
        var nativeApi = new FakeWimOperations
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
        var nativeApi = new FakeWimOperations
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
        var nativeApi = new FakeWimOperations
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
        var nativeApi = new FakeWimOperations { ImageListStatus = WimImageListStatus.NotAWim };

        OfflineWimExtractResult result = await OfflineWimImage.TryExtractAsync(
            workspace.WimPath, 1, workspace.TempParent, nativeApi, logger: null, CancellationToken.None);

        Assert.Equal(OfflineWimExtractStatus.NotAWim, result.Status);
        Assert.Equal(0, nativeApi.ApplyCallCount);
    }

    [Fact]
    public async Task TryExtractAsync_WhenNotElevated_ReturnsNeedsElevationWithoutApplying()
    {
        using var workspace = new TempWorkspace();
        var nativeApi = new FakeWimOperations { Elevated = false };

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

    private sealed class FakeWimOperations : IWimOperations
    {
        public int ApplyCallCount { get; private set; }

        public int ApplyResult { get; init; } = Win32ErrorCodes.ERROR_SUCCESS;

        public bool Elevated { get; init; } = true;

        public List<string>? Events { get; init; }

        public WimImageListStatus ImageListStatus { get; init; } = WimImageListStatus.Ok;

        public IReadOnlyList<WimImageEntry> Images { get; init; } = [new WimImageEntry(1, "Image", "Edition", null)];

        public Action<string>? OnApply { get; init; }

        public int ReadImageListCallCount { get; private set; }

        public int ApplyImage(
            string wimPath, int imageIndex, string destinationDirectory, string scratchDirectory, CancellationToken cancellationToken, ITraceLogger? logger)
        {
            ApplyCallCount++;
            Events?.Add("apply");
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

    private sealed class SequenceRecordingTraceLogger(List<string> events) : ITraceLogger
    {
        public LogLevel MinimumLevel => LogLevel.Trace;

        public void Critical(CriticalLogHandler handler) => events.Add(handler.ToStringAndClear());

        public void Debug(DebugLogHandler handler) => events.Add(handler.ToStringAndClear());

        public void Error(ErrorLogHandler handler) => events.Add(handler.ToStringAndClear());

        public void Information(InformationLogHandler handler) => events.Add(handler.ToStringAndClear());

        public void Trace(TraceLogHandler handler) => events.Add(handler.ToStringAndClear());

        public void Warning(WarningLogHandler handler) => events.Add(handler.ToStringAndClear());
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
