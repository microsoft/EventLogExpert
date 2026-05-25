// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Modal;

/// <summary>
///     Result of <see cref="IModalCoordinator.PushAsync{TModal,TResult}" />. <c>WasOpened</c> distinguishes
///     user-completion-with-default from preempt-veto.
/// </summary>
public sealed record ModalOpenResult<TResult>(TResult? Result, bool WasOpened);
