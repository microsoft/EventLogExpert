// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Modal;

public sealed record ModalOpenResult<TResult>(TResult? Result, bool WasOpened);
