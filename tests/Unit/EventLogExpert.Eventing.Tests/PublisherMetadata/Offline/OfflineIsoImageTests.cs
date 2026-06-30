// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.PublisherMetadata.Offline;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Eventing.Tests.PublisherMetadata.Offline;

public sealed class OfflineIsoImageTests
{
    [Fact]
    public void TryMount_WhenAttachFails_ReturnsMountFailed()
    {
        using var workspace = new TempIso();
        var api = new FakeVirtualDiskOperations { Status = IsoAttachStatus.Failed };

        OfflineIsoMountResult result = OfflineIsoImage.TryMount(workspace.IsoPath, api, logger: null);

        Assert.Equal(OfflineIsoMountStatus.MountFailed, result.Status);
    }

    [Fact]
    public void TryMount_WhenAttachReportsNotAnIso_ReturnsNotAnIso()
    {
        using var workspace = new TempIso();
        var api = new FakeVirtualDiskOperations { Status = IsoAttachStatus.NotAnIso };

        OfflineIsoMountResult result = OfflineIsoImage.TryMount(workspace.IsoPath, api, logger: null);

        Assert.Equal(OfflineIsoMountStatus.NotAnIso, result.Status);
    }

    [Fact]
    public void TryMount_WhenFileMissing_ReturnsMountFailed()
    {
        var api = new FakeVirtualDiskOperations();

        OfflineIsoMountResult result = OfflineIsoImage.TryMount(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".iso"), api, logger: null);

        Assert.Equal(OfflineIsoMountStatus.MountFailed, result.Status);
        Assert.False(api.Attached);
    }

    [Fact]
    public void TryMount_WhenInstallWimPresent_ReturnsMountedAndDetachesOnDispose()
    {
        using var workspace = new TempIso(installWim: true);
        var lease = new TrackingLease();
        var api = new FakeVirtualDiskOperations { VolumeRoot = workspace.VolumeRoot, Lease = lease };

        OfflineIsoMountResult result = OfflineIsoImage.TryMount(workspace.IsoPath, api, logger: null);

        Assert.Equal(OfflineIsoMountStatus.Mounted, result.Status);
        Assert.EndsWith("install.wim", result.Image!.InstallImagePath);
        Assert.False(lease.Disposed);

        result.Image.Dispose();
        result.Image.Dispose();

        Assert.True(lease.Disposed);
    }

    [Fact]
    public void TryMount_WhenMounted_StreamsMountingBeforeAttachAndMountedAfter()
    {
        using var workspace = new TempIso(installWim: true);
        var events = new List<string>();
        var logger = new SequenceRecordingTraceLogger(events);
        var api = new FakeVirtualDiskOperations { VolumeRoot = workspace.VolumeRoot, Lease = new TrackingLease(), Events = events };

        OfflineIsoMountResult result = OfflineIsoImage.TryMount(workspace.IsoPath, api, logger);

        Assert.Equal(OfflineIsoMountStatus.Mounted, result.Status);
        var relevant = events
            .Where(entry => entry == "attach"
                || entry.StartsWith("Mounting ISO", StringComparison.Ordinal)
                || entry.StartsWith("Mounted ISO at", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(3, relevant.Count);
        Assert.StartsWith("Mounting ISO", relevant[0], StringComparison.Ordinal);
        Assert.Equal("attach", relevant[1]);
        Assert.StartsWith("Mounted ISO at", relevant[2], StringComparison.Ordinal);
    }

    [Fact]
    public void TryMount_WhenNoInstallImage_ReturnsNoInstallImageAndDetaches()
    {
        using var workspace = new TempIso();
        var lease = new TrackingLease();
        var api = new FakeVirtualDiskOperations { VolumeRoot = workspace.VolumeRoot, Lease = lease };

        OfflineIsoMountResult result = OfflineIsoImage.TryMount(workspace.IsoPath, api, logger: null);

        Assert.Equal(OfflineIsoMountStatus.NoInstallImage, result.Status);
        Assert.True(lease.Disposed);
    }

    [Fact]
    public void TryMount_WhenOnlyEsdPresent_ReturnsMountedWithEsd()
    {
        using var workspace = new TempIso(installEsd: true);
        var api = new FakeVirtualDiskOperations { VolumeRoot = workspace.VolumeRoot, Lease = new TrackingLease() };

        OfflineIsoMountResult result = OfflineIsoImage.TryMount(workspace.IsoPath, api, logger: null);

        Assert.Equal(OfflineIsoMountStatus.Mounted, result.Status);
        Assert.EndsWith("install.esd", result.Image!.InstallImagePath);
    }

    private sealed class FakeVirtualDiskOperations : IVirtualDiskOperations
    {
        public bool Attached { get; private set; }

        public List<string>? Events { get; init; }

        public IDisposable Lease { get; init; } = new TrackingLease();

        public IsoAttachStatus Status { get; init; } = IsoAttachStatus.Attached;

        public string VolumeRoot { get; init; } = "";

        public IsoAttachResult Attach(string isoPath, ITraceLogger? logger)
        {
            Attached = true;
            Events?.Add("attach");

            return Status == IsoAttachStatus.Attached
                ? new IsoAttachResult(IsoAttachStatus.Attached, VolumeRoot, Lease)
                : IsoAttachResult.Failed(Status);
        }

        public VhdxAttachResult AttachVhdx(string vhdxPath, ITraceLogger? logger) =>
            throw new NotSupportedException("This fake covers ISO attach only.");
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

    private sealed class TempIso : IDisposable
    {
        public TempIso(bool installWim = false, bool installEsd = false)
        {
            VolumeRoot = Path.Combine(Path.GetTempPath(), "elx_isotest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(VolumeRoot, "sources"));
            IsoPath = Path.Combine(VolumeRoot, "image.iso");
            File.WriteAllText(IsoPath, "not a real iso - the fake ignores the content");

            if (installWim) { File.WriteAllText(Path.Combine(VolumeRoot, "sources", "install.wim"), "wim"); }

            if (installEsd) { File.WriteAllText(Path.Combine(VolumeRoot, "sources", "install.esd"), "esd"); }
        }

        public string IsoPath { get; }

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
