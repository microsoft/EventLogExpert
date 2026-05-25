// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Modal;

/// <summary>
///     Strongly-typed identifier for an active modal session. Wraps the per-show counter assigned by
///     <see cref="IModalService" />.
/// </summary>
public readonly record struct ModalId(long Value)
{
    public static ModalId None => new(0);

    public bool IsNone => Value == 0;
}
