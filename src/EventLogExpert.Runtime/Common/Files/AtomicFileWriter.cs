// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Common.Files;

/// <summary>
///     Writes a file atomically: the caller's content is streamed into a fixed-name sibling temp file in the
///     destination's own directory, then committed with an atomic same-volume
///     <see cref="File.Move(string, string, bool)" /> (overwrite). Because the temp always lives on the same volume as the
///     destination, the commit is an atomic replace-by-rename (MoveFileEx MOVEFILE_REPLACE_EXISTING): the destination
///     always ends up as either its prior content or the fully written new content -- never missing or partial -- even
///     when the commit itself fails. A cancellation or a failed write leaves the destination untouched and always cleans
///     up the temp (the temp is deleted on every path that does not commit). This is pure <see cref="System.IO" /> with no
///     WinRT dependency, so it works unchanged under elevated (packaged or unpackaged) processes.
/// </summary>
public static class AtomicFileWriter
{
    public static async Task WriteAsync(
        string destinationPath,
        Func<Stream, CancellationToken, Task> writeContent,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(destinationPath);
        ArgumentNullException.ThrowIfNull(writeContent);

        cancellationToken.ThrowIfCancellationRequested();

        var directory = Path.GetDirectoryName(destinationPath);

        if (string.IsNullOrEmpty(directory))
        {
            throw new ArgumentException(
                "The destination path must include a directory component.", nameof(destinationPath));
        }

        // Fixed-length sibling name (independent of the destination filename) so that a near-maximum-length
        // destination name cannot push the temp path past the file-system's path-component limit.
        var tempPath = Path.Combine(directory, $".eventlogexpert-{Guid.NewGuid():N}.tmp");
        var committed = false;

        try
        {
            // Explicit braces: the temp stream MUST be disposed (its handle released) BEFORE the commit, otherwise the
            // File.Move replace fails with a sharing violation. Do not collapse to `await using var`.
            await using (var tempStream = new FileStream(tempPath, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                // Asynchronous: writeContent streams via WriteAsync, so use true overlapped I/O rather than sync I/O
                // dispatched to the thread pool (matters for large exports; mirrors DebugLogFileReader's convention).
                Options = FileOptions.Asynchronous,
            }))
            {
                await writeContent(tempStream, cancellationToken);
            }

            // Final cancellation gate: the last point at which a Cancel can abort the save. After this the commit is
            // an uncancellable rename.
            cancellationToken.ThrowIfCancellationRequested();

            Commit(tempPath, destinationPath);
            committed = true;
        }
        finally
        {
            if (!committed) { TryDelete(tempPath); }
        }
    }

    private static void Commit(string tempPath, string destinationPath) =>
        // Atomic same-volume replace-by-rename. MoveFileEx(MOVEFILE_REPLACE_EXISTING) either fully succeeds or leaves
        // both files intact, so a failed commit can never destroy the existing destination. We deliberately do NOT use
        // File.Replace: its underlying ReplaceFile has documented partial-failure states (e.g.
        // ERROR_UNABLE_TO_MOVE_REPLACEMENT_2) that can delete the destination while orphaning the replacement under the
        // temp name -- a data-loss hazard in a save path. The committed file takes the destination directory's inherited
        // ACL, the correct outcome for freshly written export content (the comdlg32 Save dialog's OFN_OVERWRITEPROMPT
        // already obtained the user's consent to overwrite an existing file).
        File.Move(tempPath, destinationPath, overwrite: true);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best-effort: the temp is never the saved output, so a failed cleanup must not mask the real result.
        }
    }
}
