// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Interfaces;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Components;

public sealed partial class BannerHost : ComponentBase, IDisposable
{
    private CancellationTokenSource? _copiedFeedbackCts;
    private string? _recoveryFailureMessage;
    private string? _restartFailureMessage;
    private bool _showCopiedFeedback;

    [Inject] private IApplicationRestartService ApplicationRestartService { get; init; } = null!;

    [Inject] private IBannerService BannerService { get; init; } = null!;

    [Inject] private IClipboardService ClipboardService { get; init; } = null!;

    [Inject] private ITraceLogger TraceLogger { get; init; } = null!;

    public void Dispose()
    {
        BannerService.StateChanged -= OnStateChanged;
        CancellationTokenSource? cts = _copiedFeedbackCts;
        _copiedFeedbackCts = null;
        cts?.Cancel();
        cts?.Dispose();
    }

    protected override void OnInitialized()
    {
        BannerService.StateChanged += OnStateChanged;
        base.OnInitialized();
    }

    private async Task OnCopyDetailsClickedAsync(Exception ex)
    {
        await ClipboardService.CopyTextAsync(ex.ToString());

        CancellationTokenSource? previous = _copiedFeedbackCts;
        _copiedFeedbackCts = null;
        previous?.CancelAsync();
        previous?.Dispose();

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
        catch (TaskCanceledException) { }
    }

    private void OnDismissCritical(Guid id) => BannerService.DismissCritical(id);

    private void OnDismissInfo(Guid id) => BannerService.DismissInfoBanner(id);

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
            await BannerService.TryRecoverAsync();
        }
        catch (Exception ex)
        {
            _recoveryFailureMessage = $"Recovery failed: {ex.Message}";
            TraceLogger.Error($"{nameof(BannerHost)}.{nameof(OnReloadClickedAsync)}: recovery threw: {ex}");
            StateHasChanged();
        }
    }

    private void OnStateChanged() => _ = InvokeAsync(StateHasChanged);
}
