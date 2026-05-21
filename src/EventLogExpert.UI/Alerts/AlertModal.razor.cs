// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Modal;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.Alerts;

/// <summary>
///     Standalone alert modal used by <c>AlertDialogService</c> when no host modal is active. Dismiss-only when
///     <see cref="AcceptLabel" /> is null; otherwise renders Accept/Cancel.
/// </summary>
public sealed partial class AlertModal : ModalBase<bool>
{
    [Parameter] public string? AcceptLabel { get; set; }

    [Parameter] public string CancelLabel { get; set; } = "OK";

    [Parameter] public string Message { get; set; } = string.Empty;

    [Parameter] public string Title { get; set; } = string.Empty;

    private string AriaLabelText => string.IsNullOrEmpty(Title) ? "Alert" : Title;

    // In dismiss-only mode the single button represents a dismissal, so complete with false to
    // match Cancel/Esc and keep all dismissal routes equivalent.
    private Task HandleAcceptClickedAsync() => CompleteAsync(!string.IsNullOrEmpty(AcceptLabel));

    private Task HandleCancelClickedAsync() => CompleteAsync(false);
}
