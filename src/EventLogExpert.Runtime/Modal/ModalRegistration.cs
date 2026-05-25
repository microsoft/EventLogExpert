// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Alerts;

namespace EventLogExpert.Runtime.Modal;

public sealed class ModalRegistration
{
    public ModalRegistration(
        ModalId modalId,
        Func<ModalCloseRequest, Task<bool>> requestClose,
        ModalScope scope,
        IInlineAlertHost? inlineAlertHost)
    {
        ArgumentNullException.ThrowIfNull(requestClose);

        ModalId = modalId;
        RequestClose = requestClose;
        Scope = scope;
        InlineAlertHost = inlineAlertHost;
    }

    public IInlineAlertHost? InlineAlertHost { get; }

    public ModalId ModalId { get; }

    public Func<ModalCloseRequest, Task<bool>> RequestClose { get; }

    public ModalScope Scope { get; }
}
