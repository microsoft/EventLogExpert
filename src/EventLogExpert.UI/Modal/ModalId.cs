// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Modal;

public readonly record struct ModalId(long Value)
{
    public static ModalId None => new(0);

    public bool IsNone => Value == 0;
}
