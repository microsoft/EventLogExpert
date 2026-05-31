// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Modal;

public enum FooterPreset
{
    CloseOnly,
    SaveCancel,
    ImportExportClose,
    Dismiss,
    AcceptCancel,

    /// <remarks>
    ///     When <c>None</c> is selected, consumers MUST provide <c>ExtraFooterContent</c> with dismissal controls OR wire
    ///     <c>OnDialogClosedByUser</c>; otherwise the modal cannot be dismissed except via Esc.
    /// </remarks>
    None,
}
