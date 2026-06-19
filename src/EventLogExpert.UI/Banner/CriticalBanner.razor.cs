// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.Common.Restart;
using EventLogExpert.UI.Focus;
using EventLogExpert.UI.Inputs;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.Banner;

public sealed partial class CriticalBanner : ComponentBase, IDisposable
{
    private CancellationTokenSource? _copiedFeedbackCts;
    private string? _recoveryFailureMessage;
    private Button? _reloadButton;
    private string? _restartFailureMessage;
    private bool _showCopiedFeedback;

    [Parameter] public Exception Critical { get; set; } = null!;

    [Inject] private IApplicationRestartService ApplicationRestartService { get; init; } = null!;

    [Inject] private IClipboardService ClipboardService { get; init; } = null!;

    [Inject] private ICriticalErrorService CriticalErrorService { get; init; } = null!;

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
            await Task.Delay(TimeSpan.FromSeconds(2), cts.Token);

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
        _recoveryFailureMessage = null;
        _restartFailureMessage = null;

        bool success = await ApplicationRestartService.TryRestartAsync();

        if (!success)
        {
            _restartFailureMessage = "Restart failed; please close and reopen manually.";
            StateHasChanged();
        }
    }

    private async Task OnReloadClickedAsync()
    {
        _recoveryFailureMessage = null;
        _restartFailureMessage = null;

        try
        {
            await CriticalErrorService.TryRecoverAsync();
        }
        catch (Exception ex)
        {
            _recoveryFailureMessage = $"Recovery failed: {ex.Message}";

            TraceLogger.Error($"{nameof(CriticalBanner)}.{nameof(OnReloadClickedAsync)}: recovery threw: {ex}");

            StateHasChanged();
        }
    }
}
