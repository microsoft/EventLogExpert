// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Shared.Base;

/// <summary>Predefined footer button layouts for <see cref="ModalChrome"/>.</summary>
public enum FooterPreset
{
    CloseOnly,
    SaveCancel,
    ImportExportClose,

    /// <summary>Single accept-style button — caller-provided label.</summary>
    Dismiss,

    /// <summary>Two buttons (accept/cancel) — caller-provided labels.</summary>
    AcceptCancel
}
