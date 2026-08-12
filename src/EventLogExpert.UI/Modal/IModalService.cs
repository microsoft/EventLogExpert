// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.Modal;

public interface IModalService
{
    event Action? StateChanged;

    ModalId ActiveModalId { get; }

    IDictionary<string, object?>? ActiveModalParameters { get; }

    Type? ActiveModalType { get; }

    void CancelActive();

    void Complete<TResult>(ModalId modalId, TResult? result);

    Task<TResult?> Show<TModal, TResult>(IDictionary<string, object?>? parameters = null)
        where TModal : IComponent;
}
