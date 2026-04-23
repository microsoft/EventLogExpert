// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Services;

/// <summary>
///     Routes <see cref="IAlertDialogService" /> calls through <see cref="IModalService" />. Active modals exposing
///     <see cref="IInlineAlertHost" /> get the request as an inline banner; otherwise a standalone alert/prompt modal is
///     opened via the supplied delegates. All routing is marshaled to the main thread so background callers
///     (UpdateService, DeploymentService) are safe.
/// </summary>
public sealed class ModalAlertDialogService(
    IModalService modalService,
    IMainThreadService mainThreadService,
    Func<IReadOnlyDictionary<string, object?>, Task<bool>> openStandaloneAlert,
    Func<IReadOnlyDictionary<string, object?>, Task<string>> openStandalonePrompt) : IAlertDialogService
{
    private readonly IMainThreadService _mainThreadService = mainThreadService;
    private readonly IModalService _modalService = modalService;
    private readonly Func<IReadOnlyDictionary<string, object?>, Task<bool>> _openStandaloneAlert = openStandaloneAlert;
    private readonly Func<IReadOnlyDictionary<string, object?>, Task<string>> _openStandalonePrompt =
        openStandalonePrompt;

    public Task<string> DisplayPrompt(string title, string message) => DisplayPromptCore(title, message, null);

    public Task<string> DisplayPrompt(string title, string message, string initialValue) =>
        DisplayPromptCore(title, message, initialValue);

    public async Task ShowAlert(string title, string message, string cancel) =>
        await InvokeOnMainThreadAsync(async () =>
        {
            if (!_modalService.TryGetActiveAlertHost(out var host))
            {
                return await _openStandaloneAlert(new Dictionary<string, object?>
                {
                    ["Title"] = title,
                    ["Message"] = message,
                    ["AcceptLabel"] = null,
                    ["CancelLabel"] = cancel,
                });
            }

            try
            {
                InlineAlertResult result = await host!.ShowInlineAlertAsync(
                    new InlineAlertRequest(title, message, null, cancel, false, null),
                    CancellationToken.None);

                return result.Accepted;
            }
            catch (TaskCanceledException)
            {
                return false;
            }
        });

    public Task<bool> ShowAlert(string title, string message, string accept, string cancel) =>
        InvokeOnMainThreadAsync(async () =>
        {
            if (!_modalService.TryGetActiveAlertHost(out var host))
            {
                return await _openStandaloneAlert(new Dictionary<string, object?>
                {
                    ["Title"] = title,
                    ["Message"] = message,
                    ["AcceptLabel"] = accept,
                    ["CancelLabel"] = cancel,
                });
            }

            try
            {
                InlineAlertResult result = await host!.ShowInlineAlertAsync(
                    new InlineAlertRequest(title, message, accept, cancel, false, null),
                    CancellationToken.None);

                return result.Accepted;
            }
            catch (TaskCanceledException)
            {
                return false;
            }
        });

    private Task<string> DisplayPromptCore(string title, string message, string? initialValue) =>
        InvokeOnMainThreadAsync(async () =>
        {
            if (!_modalService.TryGetActiveAlertHost(out var host))
            {
                return await _openStandalonePrompt(new Dictionary<string, object?>
                {
                    ["Title"] = title,
                    ["Message"] = message,
                    ["InitialValue"] = initialValue ?? string.Empty,
                });
            }

            try
            {
                InlineAlertResult result = await host!.ShowInlineAlertAsync(
                    new InlineAlertRequest(title, message, "OK", "Cancel", true, initialValue),
                    CancellationToken.None);

                return result.Accepted ? result.PromptValue ?? string.Empty : string.Empty;
            }
            catch (TaskCanceledException)
            {
                return string.Empty;
            }
        });

    private async Task<TResult> InvokeOnMainThreadAsync<TResult>(Func<Task<TResult>> action)
    {
        TResult result = default!;
        
        await _mainThreadService.InvokeOnMainThreadAsync(async () => { result = await action(); });

        return result;
    }
}
