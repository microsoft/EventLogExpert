// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Common.Clipboard;

public interface IClipboardService
{
    /// <summary>
    ///     Best-effort copy of the current selection (one or more events), or the focused event when nothing is selected,
    ///     to the clipboard. Implementations must not throw; any failure is logged internally so callers can invoke without
    ///     try/catch.
    /// </summary>
    Task CopySelectedEvent(EventCopyFormat? format = null);

    /// <summary>
    ///     Best-effort copy of the supplied text to the clipboard. Implementations must not throw; any failure is logged
    ///     internally so callers can invoke without try/catch.
    /// </summary>
    Task CopyTextAsync(string text);
}
