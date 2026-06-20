// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Common.Threading;
using EventLogExpert.Runtime.Modal;

namespace EventLogExpert.Runtime.Alerts;

public sealed class AlertDialogService(
    IModalCoordinator modalCoordinator,
    IMainThreadService mainThreadService,
    IErrorBannerService errorBannerService,
    IInfoBannerService infoBannerService,
    Func<IReadOnlyDictionary<string, object?>, Task<bool>> openStandaloneAlert,
    Func<IReadOnlyDictionary<string, object?>, Task<string>> openStandalonePrompt) : IAlertDialogService
{
    private readonly IErrorBannerService _errorBannerService = errorBannerService;
    private readonly IInfoBannerService _infoBannerService = infoBannerService;
    private readonly IMainThreadService _mainThreadService = mainThreadService;
    private readonly IModalCoordinator _modalCoordinator = modalCoordinator;
    private readonly Func<IReadOnlyDictionary<string, object?>, Task<bool>> _openStandaloneAlert = openStandaloneAlert;
    private readonly Func<IReadOnlyDictionary<string, object?>, Task<string>> _openStandalonePrompt =
        openStandalonePrompt;

    public Task<string> DisplayPrompt(string title, string message) => DisplayPromptCore(title, message, null, null);

    public Task<string> DisplayPrompt(string title, string message, string initialValue) =>
        DisplayPromptCore(title, message, initialValue, null);

    public Task<string> DisplayPrompt(string title, string message, string initialValue, Func<string, string?>? validate) =>
        DisplayPromptCore(title, message, initialValue, validate);

    public Task ShowAlert(string title, string message, string cancel) =>
        ShowAlert(title, message, cancel, AlertPresentation.Auto);

    public Task ShowAlert(string title, string message, string cancel, AlertPresentation presentation) =>
        ShowAlertCore(title, message, null, cancel, presentation);

    public Task<bool> ShowAlert(string title, string message, string accept, string cancel) =>
        ShowAlert(title, message, accept, cancel, AlertPresentation.Auto);

    public Task<bool> ShowAlert(
        string title,
        string message,
        string accept,
        string cancel,
        AlertPresentation presentation)
    {
        if (presentation == AlertPresentation.Banner)
        {
            throw new ArgumentException(
                $"{nameof(AlertPresentation)}.{nameof(AlertPresentation.Banner)} is not valid for two-button alerts " +
                "(the banner has no accept/cancel pair).",
                nameof(presentation));
        }

        return ShowAlertCore(title, message, accept, cancel, presentation);
    }

    public Task ShowErrorAlert(string title, string message, string? actionLabel = null, Func<Task>? action = null)
    {
        _errorBannerService.ReportError(title, message, actionLabel, action);

        return Task.CompletedTask;
    }

    private Task<string> DisplayPromptCore(string title, string message, string? initialValue, Func<string, string?>? validate) =>
        InvokeOnMainThreadAsync(async () =>
        {
            if (!_modalCoordinator.TryGetInlineAlertHost(out var host))
            {
                return await _openStandalonePrompt(new Dictionary<string, object?>
                {
                    ["Title"] = title,
                    ["Message"] = message,
                    ["InitialValue"] = initialValue ?? string.Empty,
                    ["Validate"] = validate,
                });
            }

            try
            {
                InlineAlertResult result = await host.ShowInlineAlertAsync(
                    new InlineAlertRequest(title, message, "OK", "Cancel", true, initialValue)
                    {
                        Validate = validate,
                    },
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

    private Task<bool> ShowAlertCore(
        string title,
        string message,
        string? accept,
        string cancel,
        AlertPresentation presentation)
    {
        if (presentation == AlertPresentation.Banner)
        {
            _infoBannerService.ReportInfoBanner(title, message, BannerSeverity.Warning);

            return Task.FromResult(false);
        }

        return InvokeOnMainThreadAsync(async () =>
        {
            _modalCoordinator.TryGetInlineAlertHost(out var host);

            if (presentation == AlertPresentation.InlineOnly && host is null)
            {
                throw new InvalidOperationException(
                    $"{nameof(AlertPresentation)}.{nameof(AlertPresentation.InlineOnly)} requires an active inline " +
                    "alert host but none is registered.");
            }

            if (presentation == AlertPresentation.PopupOnly)
            {
                host = null;
            }

            if (host is null)
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
                InlineAlertResult result = await host.ShowInlineAlertAsync(
                    new InlineAlertRequest(title, message, accept, cancel, false, null),
                    CancellationToken.None);

                return result.Accepted;
            }
            catch (TaskCanceledException)
            {
                return false;
            }
        });
    }
}
