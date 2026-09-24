// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.OfflineImaging.Workspace;

public enum OfflineWriteProbeStatus
{
    Writable,
    ControlledFolderAccessBlocked,
    IoError
}

public readonly record struct OfflineWriteProbeResult(
    OfflineWriteProbeStatus Status,
    string Directory,
    string? IoDetail)
{
    public bool IsWritable => Status == OfflineWriteProbeStatus.Writable;
}

// Use LocalAppData scratch instead of Temp because Controlled Folder Access can block the elevated helper.
public static class OfflineScratch
{
    public static string Root =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EventLogExpert",
            "Scratch");

    // Preflight writes fail fast for CFA/ACL denial before a long native apply can wedge.
    public static OfflineWriteProbeResult ProbeWritable(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);

            string probePath = Path.Combine(directory, $".elx-write-probe-{Guid.NewGuid():N}.tmp");

            // CreateNew plus one byte surfaces CFA/ACL denial here; DeleteOnClose removes the probe.
            using var probe = new FileStream(probePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);

            probe.WriteByte(0);

            return new OfflineWriteProbeResult(OfflineWriteProbeStatus.Writable, directory, IoDetail: null);
        }
        catch (UnauthorizedAccessException)
        {
            return new OfflineWriteProbeResult(OfflineWriteProbeStatus.ControlledFolderAccessBlocked,
                directory,
                IoDetail: null);
        }
        catch (IOException ex)
        {
            return new OfflineWriteProbeResult(OfflineWriteProbeStatus.IoError, directory, ex.Message);
        }
    }
}
