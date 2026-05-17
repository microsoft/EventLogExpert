// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Alerts;

namespace EventLogExpert.Runtime.Common.Files;

public interface IFileSaveService
{
    /// <summary>
    ///     Opens a system "Save As" dialog and, only after the user confirms a destination, invokes
    ///     <paramref name="writeContent" /> against a writable stream whose contents are then saved to the destination.
    ///     Returns the saved path on success, or <c>null</c> if the user cancelled (in which case
    ///     <paramref name="writeContent" /> is never invoked). Throws if the user picked a path but the write or completion
    ///     failed, or if the host environment cannot present a save dialog (e.g., no MAUI window available); callers should
    ///     handle failures via try/catch and surface them through <see cref="IAlertDialogService" />.
    /// </summary>
    /// <param name="suggestedFileName">Default filename shown in the dialog (e.g., "debug-log.log").</param>
    /// <param name="fileTypes">
    ///     Group label to extension list (e.g., "Log files" -> [".log", ".txt"]). Must contain at least
    ///     one entry.
    /// </param>
    /// <param name="writeContent">
    ///     Asynchronous writer invoked exactly once with a writable stream after the user confirms a
    ///     destination. Implementations may stream directly to disk or buffer the entire output in memory before writing to
    ///     the picked file (the MAUI implementation buffers so that a writer exception leaves the picked file untouched);
    ///     callers should keep exports reasonably bounded to fit in memory. The stream is owned by the service and disposed
    ///     once the writer completes; callers must not retain or dispose it.
    /// </param>
    Task<string?> SaveAsync(
        string suggestedFileName,
        IReadOnlyDictionary<string, IReadOnlyList<string>> fileTypes,
        Func<Stream, Task> writeContent);
}
