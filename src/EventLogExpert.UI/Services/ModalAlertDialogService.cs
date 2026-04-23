// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Services;

/// <summary>
/// Routes <see cref="IAlertDialogService"/> calls through <see cref="IModalService"/>. When an
/// active modal exposes an inline-alert host (<see cref="IInlineAlertHost"/>) the request is shown
/// as a banner inside that modal so the user's context is preserved; otherwise an alert/prompt
/// modal is opened as its own modal via the supplied <paramref name="openStandaloneAlert"/> /
/// <paramref name="openStandalonePrompt"/> delegates.
/// </summary>
/// <remarks>
/// Background callers (e.g., <c>UpdateService</c>, <c>DeploymentService</c>) may invoke from
/// non-UI threads. All routing is marshaled to the main thread so Blazor render dispatch is safe.
/// The standalone-modal openers are injected as delegates so concrete modal types stay in the
/// MAUI app project while the routing logic remains testable from
/// <c>EventLogExpert.UI.Tests</c>.
/// </remarks>
public sealed class ModalAlertDialogService(
    IModalService modalService,
    IMainThreadService mainThreadService,
    Func<IReadOnlyDictionary<string, object?>, Task<bool>> openStandaloneAlert,
    Func<IReadOnlyDictionary<string, object?>, Task<string>> openStandalonePrompt) : IAlertDialogService
{
    private readonly IMainThreadService _mainThreadService = mainThreadService;
    private readonly IModalService _modalService = modalService;
    private readonly Func<IReadOnlyDictionary<string, object?>, Task<bool>> _openStandaloneAlert = openStandaloneAlert;
    private readonly Func<IReadOnlyDictionary<string, object?>, Task<string>> _openStandalonePrompt = openStandalonePrompt;

    public Task<string> DisplayPrompt(string title, string message) =>
        DisplayPromptCore(title, message, initialValue: null);

    public Task<string> DisplayPrompt(string title, string message, string initialValue) =>
        DisplayPromptCore(title, message, initialValue);

    public async Task ShowAlert(string title, string message, string cancel) =>
        await InvokeOnMainThreadAsync<bool>(async () =>
        {
            if (_modalService.TryGetActiveAlertHost(out var host))
            {
                try
                {
                    InlineAlertResult result = await host!.ShowInlineAlertAsync(
                        new InlineAlertRequest(title, message, AcceptLabel: null, CancelLabel: cancel, IsPrompt: false, PromptInitialValue: null),
                        CancellationToken.None);
                    return result.Accepted;
                }
                catch (TaskCanceledException)
                {
                    return false;
                }
            }

            return await _openStandaloneAlert(new Dictionary<string, object?>
            {
                ["Title"] = title,
                ["Message"] = message,
                ["AcceptLabel"] = null,
                ["CancelLabel"] = cancel,
            });
        });

    public Task<bool> ShowAlert(string title, string message, string accept, string cancel) =>
        InvokeOnMainThreadAsync(async () =>
        {
            if (_modalService.TryGetActiveAlertHost(out var host))
            {
                try
                {
                    InlineAlertResult result = await host!.ShowInlineAlertAsync(
                        new InlineAlertRequest(title, message, AcceptLabel: accept, CancelLabel: cancel, IsPrompt: false, PromptInitialValue: null),
                        CancellationToken.None);
                    return result.Accepted;
                }
                catch (TaskCanceledException)
                {
                    return false;
                }
            }

            return await _openStandaloneAlert(new Dictionary<string, object?>
            {
                ["Title"] = title,
                ["Message"] = message,
                ["AcceptLabel"] = accept,
                ["CancelLabel"] = cancel,
            });
        });

    private Task<string> DisplayPromptCore(string title, string message, string? initialValue) =>
        InvokeOnMainThreadAsync(async () =>
        {
            if (_modalService.TryGetActiveAlertHost(out var host))
            {
                try
                {
                    InlineAlertResult result = await host!.ShowInlineAlertAsync(
                        new InlineAlertRequest(title, message, AcceptLabel: "OK", CancelLabel: "Cancel", IsPrompt: true, PromptInitialValue: initialValue),
                        CancellationToken.None);
                    return result.Accepted ? result.PromptValue ?? string.Empty : string.Empty;
                }
                catch (TaskCanceledException)
                {
                    return string.Empty;
                }
            }

            return await _openStandalonePrompt(new Dictionary<string, object?>
            {
                ["Title"] = title,
                ["Message"] = message,
                ["InitialValue"] = initialValue ?? string.Empty,
            });
        });

    private async Task<TResult> InvokeOnMainThreadAsync<TResult>(Func<Task<TResult>> action)
    {
        TResult result = default!;
        await _mainThreadService.InvokeOnMainThreadAsync(async () => { result = await action(); });
        return result;
    }
}
