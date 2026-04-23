// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Shared.Base;

/// <summary>
///     Predefined footer button layouts used by <see cref="ModalChrome" />. Matches the existing IAlertDialogService
///     accept/cancel terminology so labels stay consistent.
/// </summary>
public enum FooterPreset
{
    /// <summary>Single close button.</summary>
    CloseOnly,

    /// <summary>Save + Cancel/Exit.</summary>
    SaveCancel,

    /// <summary>Import + Export buttons on the left, Close on the right.</summary>
    ImportExportClose,

    /// <summary>Single accept-style button (e.g., "OK") — caller-provided label.</summary>
    Dismiss,

    /// <summary>Two buttons (e.g., "Accept" / "Cancel") — caller-provided labels.</summary>
    AcceptCancel
}
