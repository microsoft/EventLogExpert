// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.OfflineImaging.VirtualDisk;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Eventing.OfflineImaging.Tests.VirtualDisk;

public sealed class OfflineVhdxImageTests
{
    [Fact]
    public void TryMount_WhenAttachFails_ReturnsMountFailed()
    {
        using var workspace = new TempVhdx();
        var api = new FakeVirtualDiskOperations { Status = VhdxAttachStatus.Failed };

        OfflineVhdxMountResult result = OfflineVhdxImage.TryMount(workspace.VhdxPath, api, logger: null);

        Assert.Equal(OfflineVhdxMountStatus.MountFailed, result.Status);
    }

    [Fact]
    public void TryMount_WhenAttachReportsNotAVhdx_ReturnsNotAVhdx()
    {
        using var workspace = new TempVhdx();
        var api = new FakeVirtualDiskOperations { Status = VhdxAttachStatus.NotAVhdx };

        OfflineVhdxMountResult result = OfflineVhdxImage.TryMount(workspace.VhdxPath, api, logger: null);

        Assert.Equal(OfflineVhdxMountStatus.NotAVhdx, result.Status);
    }

    [Fact]
    public void TryMount_WhenFileMissing_ReturnsMountFailed()
    {
        var api = new FakeVirtualDiskOperations();

        OfflineVhdxMountResult result = OfflineVhdxImage.TryMount(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".vhdx"), api, logger: null);

        Assert.Equal(OfflineVhdxMountStatus.MountFailed, result.Status);
        Assert.False(api.Attached);
    }

    [Fact]
    public void TryMount_WhenMultipleWindowsVolumes_ReturnsMultiple()
    {
        using var workspace = new TempVhdx();
        var api = new FakeVirtualDiskOperations { Status = VhdxAttachStatus.MultipleWindowsVolumes };

        OfflineVhdxMountResult result = OfflineVhdxImage.TryMount(workspace.VhdxPath, api, logger: null);

        Assert.Equal(OfflineVhdxMountStatus.MultipleWindowsVolumes, result.Status);
    }

    [Fact]
    public void TryMount_WhenNoWindowsVolume_ReturnsNoWindowsVolume()
    {
        using var workspace = new TempVhdx();
        var api = new FakeVirtualDiskOperations { Status = VhdxAttachStatus.NoWindowsVolume };

        OfflineVhdxMountResult result = OfflineVhdxImage.TryMount(workspace.VhdxPath, api, logger: null);

        Assert.Equal(OfflineVhdxMountStatus.NoWindowsVolume, result.Status);
    }

    [Fact]
    public void TryMount_WhenWindowsVolumeResolved_ReturnsMountedAndDetachesOnDispose()
    {
        using var workspace = new TempVhdx();
        var lease = new TrackingLease();
        var api = new FakeVirtualDiskOperations { VolumeRoot = workspace.VolumeRoot, Lease = lease };

        OfflineVhdxMountResult result = OfflineVhdxImage.TryMount(workspace.VhdxPath, api, logger: null);

        Assert.Equal(OfflineVhdxMountStatus.Mounted, result.Status);
        Assert.Equal(workspace.VolumeRoot, result.Image!.VolumeRoot);
        Assert.False(lease.Disposed);

        result.Image.Dispose();
        result.Image.Dispose();

        Assert.True(lease.Disposed);
    }

    [Fact]
    public void TryMount_WhenWindowsVolumeResolved_StreamsMountingBeforeAttachAndMountedAfter()
    {
        using var workspace = new TempVhdx();
        var events = new List<string>();
        var logger = new SequenceRecordingTraceLogger(events);
        var api = new FakeVirtualDiskOperations { VolumeRoot = workspace.VolumeRoot, Lease = new TrackingLease(), Events = events };

        OfflineVhdxMountResult result = OfflineVhdxImage.TryMount(workspace.VhdxPath, api, logger);

        Assert.Equal(OfflineVhdxMountStatus.Mounted, result.Status);
        var relevant = events
            .Where(entry => entry == "attach"
                || entry.StartsWith("Mounting VHD/VHDX", StringComparison.Ordinal)
                || entry.StartsWith("Mounted VHD/VHDX at", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(3, relevant.Count);
        Assert.StartsWith("Mounting VHD/VHDX", relevant[0], StringComparison.Ordinal);
        Assert.Equal("attach", relevant[1]);
        Assert.StartsWith("Mounted VHD/VHDX at", relevant[2], StringComparison.Ordinal);
    }

    private sealed class FakeVirtualDiskOperations : IVirtualDiskOperations
    {
        public bool Attached { get; private set; }

        public List<string>? Events { get; init; }

        public IDisposable Lease { get; init; } = new TrackingLease();

        public VhdxAttachStatus Status { get; init; } = VhdxAttachStatus.Attached;

        public string VolumeRoot { get; init; } = "";

        public IsoAttachResult Attach(string isoPath, ITraceLogger? logger) =>
            throw new NotSupportedException("This fake covers VHD/VHDX attach only.");

        public VhdxAttachResult AttachVhdx(string vhdxPath, ITraceLogger? logger)
        {
            Attached = true;
            Events?.Add("attach");

            return Status == VhdxAttachStatus.Attached
                ? new VhdxAttachResult(VhdxAttachStatus.Attached, VolumeRoot, Lease)
                : VhdxAttachResult.Failed(Status);
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

    private sealed class TempVhdx : IDisposable
    {
        public TempVhdx()
        {
            VolumeRoot = Path.Combine(Path.GetTempPath(), "elx_vhdxtest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(VolumeRoot);
            VhdxPath = Path.Combine(VolumeRoot, "disk.vhdx");
            File.WriteAllText(VhdxPath, "not a real vhdx - the fake ignores the content");
        }

        public string VhdxPath { get; }

        public string VolumeRoot { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(VolumeRoot)) { Directory.Delete(VolumeRoot, recursive: true); }
            }
            catch (IOException)
            {
                // Best-effort cleanup of the test workspace.
            }
        }
    }

    private sealed class TrackingLease : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}
