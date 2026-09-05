// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.Common.Restart;
using EventLogExpert.UI.Focus;
using EventLogExpert.UI.Inputs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.Banner;

public sealed partial class CriticalBanner : ComponentBase, IDisposable
{
    private static readonly TimeSpan s_copiedFeedbackDuration = TimeSpan.FromSeconds(2);

    private CancellationTokenSource? _copiedFeedbackCts;
    private bool _recoveryFailed;
    private string? _recoveryFailureMessage;
    private Button? _reloadButton;
    private bool _restartFailed;
    private string? _restartFailureMessage;
    private bool _showCopiedFeedback;

    [Parameter] public Exception Critical { get; set; } = null!;

    [Inject] private IApplicationRestartService ApplicationRestartService { get; init; } = null!;

    [Inject] private IClipboardService ClipboardService { get; init; } = null!;

    [Inject] private ICriticalErrorService CriticalErrorService { get; init; } = null!;

    [Inject] private IStringLocalizer<SharedResource> Localizer { get; init; } = null!;

    [Inject] private ITraceLogger TraceLogger { get; init; } = null!;

    public void Dispose()
    {
        CancellationTokenSource? cts = _copiedFeedbackCts;
        _copiedFeedbackCts = null;
        cts?.Cancel();
        cts?.Dispose();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && _reloadButton is { } reloadButton)
        {
            await ElementFocus.SafelyAsync(reloadButton.Element);
        }
    }

    private async Task OnCopyDetailsClickedAsync(Exception ex)
    {
        await ClipboardService.CopyTextAsync(ex.ToString());

        CancellationTokenSource? previous = _copiedFeedbackCts;
        _copiedFeedbackCts = null;

        if (previous is not null)
        {
            await previous.CancelAsync();
            previous.Dispose();
        }

        var cts = new CancellationTokenSource();
        _copiedFeedbackCts = cts;
        _showCopiedFeedback = true;
        StateHasChanged();

        try
        {
            await Task.Delay(s_copiedFeedbackDuration, cts.Token);

            if (ReferenceEquals(_copiedFeedbackCts, cts))
            {
                _showCopiedFeedback = false;

                StateHasChanged();
            }
        }
        catch (TaskCanceledException) { /* Feedback cycle cancelled by next copy or dispose. */ }
    }

    private async Task OnRelaunchClickedAsync()
    {
        _recoveryFailed = false;
        _recoveryFailureMessage = null;
        _restartFailed = false;
        _restartFailureMessage = null;

        bool success = await ApplicationRestartService.TryRestartAsync();

        if (!success)
        {
            _restartFailed = true;
            _restartFailureMessage = Localizer["Banner_Critical_RestartFailed"];
            StateHasChanged();
        }
    }

    private async Task OnReloadClickedAsync()
    {
        _recoveryFailed = false;
        _recoveryFailureMessage = null;
        _restartFailed = false;
        _restartFailureMessage = null;

        try
        {
            await CriticalErrorService.TryRecoverAsync();
        }
        catch (Exception ex)
        {
            _recoveryFailed = true;
            _recoveryFailureMessage = Localizer["Banner_Critical_RecoveryFailed", ex.Message];

            TraceLogger.Error($"{nameof(CriticalBanner)}.{nameof(OnReloadClickedAsync)}: recovery threw: {ex}");

            StateHasChanged();
        }
    }
}
