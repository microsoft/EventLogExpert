// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.PublisherMetadata.Offline;

/// <summary>
///     Single source of truth for the working ("scratch") folder offline-image operations write into - WIM/ESD image
///     extraction and registry-hive staging. Deliberately NOT <see cref="Path.GetTempPath" />: a user's <c>%TEMP%</c> can
///     resolve onto a folder that Windows Controlled Folder Access (ransomware protection) protects, which silently denies
///     the elevated helper's writes and can wedge a native <c>WIMApplyImage</c> mid-extract. The scratch root lives under
///     <c>%LocalAppData%\EventLogExpert\Scratch</c>, which CFA does not protect by default and which both the in-process
///     app and the standalone elevated helper compute identically (same user), so the helper's orphan-mount sweep finds
///     the same staging folders across runs.
/// </summary>
public static class OfflineScratch
{
    /// <summary>The scratch root. Stable across runs of the same user, in a folder CFA does not protect by default.</summary>
    public static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EventLogExpert",
        "Scratch");

    /// <summary>
    ///     Verifies <paramref name="directory" /> can actually be written to (creating it if missing), returning
    ///     <see langword="null" /> when writable or an actionable, user-facing message naming the folder and the likely
    ///     Controlled Folder Access remedy when not. Used as a fast pre-flight before a long native apply so a CFA/ACL denial
    ///     fails in milliseconds with a clear message instead of wedging a native call that cannot be cancelled.
    /// </summary>
    public static string? ProbeWritable(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);

            string probePath = Path.Combine(directory, $".elx-write-probe-{Guid.NewGuid():N}.tmp");

            // CreateNew + a single byte exercises an actual write; DeleteOnClose removes the probe even if a later step
            // throws. A Controlled Folder Access or ACL denial surfaces here as UnauthorizedAccessException.
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
