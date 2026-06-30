// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.PublisherMetadata.Offline;

// Use LocalAppData scratch instead of Temp because Controlled Folder Access can block the elevated helper.
public static class OfflineScratch
{
    public static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EventLogExpert",
        "Scratch");

    // Preflight writes fail fast for CFA/ACL denial before a long native apply can wedge.
    public static string? ProbeWritable(string directory)
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

            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return BuildBlockedMessage(directory, controlledFolderAccessLikely: true);
        }
        catch (IOException ex)
        {
            return $"Cannot write to '{directory}': {ex.Message}";
        }
    }

    private static string BuildBlockedMessage(string directory, bool controlledFolderAccessLikely)
    {
        string executable = Path.GetFileName(Environment.ProcessPath ?? "EventLogExpert");

        if (!controlledFolderAccessLikely)
        {
            return $"Cannot write to '{directory}'. Choose a different destination or adjust its permissions.";
        }

        return $"Cannot write to '{directory}'. This is often Controlled Folder Access (Windows ransomware protection) " +
            $"blocking {executable}. Allow it under Windows Security \u2192 Virus & threat protection \u2192 Ransomware " +
            "protection \u2192 Allow an app through Controlled folder access, or choose a different destination.";
    }
}
