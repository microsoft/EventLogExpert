// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Modal;

public enum ModalCloseReason
{
    UserDismiss,
    EscKey,
    OutsideClick,
    ProgrammaticCancel,
    OtherModalActivation
}
