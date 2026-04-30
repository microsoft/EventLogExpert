// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;

namespace EventLogExpert.UI.Services;

/// <summary>
///     Routes <see cref="IAlertDialogService" /> calls through <see cref="IModalService" />. Active modals exposing
///     <see cref="IInlineAlertHost" /> get the request as an inline banner; otherwise a standalone alert/prompt modal is
///     opened via the supplied delegates. <see cref="AlertPresentation.Banner" /> requests and
///     <see cref="ShowCriticalAlert" /> bypass the modal routing and go directly to <see cref="IBannerService" />. All
///     popup/inline routing is marshaled to the main thread so background callers (UpdateService, DeploymentService) are
///     safe; banner-routed paths are not marshaled because <see cref="IBannerService" /> is thread-safe.
/// </summary>
public sealed class ModalAlertDialogService(
    IModalService modalService,
    IMainThreadService mainThreadService,
    IBannerService bannerService,
    Func<IReadOnlyDictionary<string, object?>, Task<bool>> openStandaloneAlert,
    Func<IReadOnlyDictionary<string, object?>, Task<string>> openStandalonePrompt) : IAlertDialogService
{
    private readonly IBannerService _bannerService = bannerService;
    private readonly IMainThreadService _mainThreadService = mainThreadService;
    private readonly IModalService _modalService = modalService;
    private readonly Func<IReadOnlyDictionary<string, object?>, Task<bool>> _openStandaloneAlert = openStandaloneAlert;
    private readonly Func<IReadOnlyDictionary<string, object?>, Task<string>> _openStandalonePrompt =
        openStandalonePrompt;

    public Task<string> DisplayPrompt(string title, string message) => DisplayPromptCore(title, message, null);

    public Task<string> DisplayPrompt(string title, string message, string initialValue) =>
        DisplayPromptCore(title, message, initialValue);

    public Task ShowAlert(string title, string message, string cancel) =>
        ShowAlert(title, message, cancel, AlertPresentation.Auto);

    public Task ShowAlert(string title, string message, string cancel, AlertPresentation presentation) =>
        ShowAlertCore(title, message, accept: null, cancel, presentation);

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

    public Task ShowCriticalAlert(string title, string message)
    {
        _bannerService.ReportCritical(title, message);

        return Task.CompletedTask;
    }

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

    private Task<bool> ShowAlertCore(
        string title,
        string message,
        string? accept,
        string cancel,
        AlertPresentation presentation)
    {
        if (presentation == AlertPresentation.Banner)
        {
            _bannerService.ReportInfoBanner(title, message, BannerSeverity.Warning);

            return Task.FromResult(false);
        }

        return InvokeOnMainThreadAsync(async () =>
        {
            bool hostAvailable = _modalService.TryGetActiveAlertHost(out var host);

            if (presentation == AlertPresentation.InlineOnly && !hostAvailable)
            {
                throw new InvalidOperationException(
                    $"{nameof(AlertPresentation)}.{nameof(AlertPresentation.InlineOnly)} requires an active inline " +
                    "alert host but none is registered.");
            }

            if (presentation == AlertPresentation.PopupOnly)
            {
                hostAvailable = false;
            }

            if (!hostAvailable)
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
    }
}
