// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Shared.Base;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Components.Alerts;

/// <summary>
/// Standalone alert modal used by <c>ModalAlertDialogService</c> when no other modal is active.
/// When <see cref="AcceptLabel"/> is <c>null</c>, renders a single dismiss button (labeled with
/// <see cref="CancelLabel"/>) matching <c>IAlertDialogService.ShowAlert(title, message, cancel)</c>.
/// Otherwise renders Accept/Cancel buttons matching the two-button overload.
/// </summary>
public sealed partial class AlertModal : ModalBase<bool>
{
    [Parameter] public string? AcceptLabel { get; set; }

    [Parameter] public string CancelLabel { get; set; } = "OK";

    [Parameter] public string Message { get; set; } = string.Empty;

    [Parameter] public string Title { get; set; } = string.Empty;

    private string AriaLabelText => string.IsNullOrEmpty(Title) ? "Alert" : Title;

    private Task HandleAcceptClickedAsync() => CompleteAsync(true);

    private Task HandleCancelClickedAsync() => CompleteAsync(false);
}
