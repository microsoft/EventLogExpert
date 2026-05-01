// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace EventLogExpert.Shared.Components;

public sealed partial class BannerHost : ComponentBase, IDisposable
{
    private CancellationTokenSource? _copiedFeedbackCts;
    private BannerView _currentView;
    private BannerView _previousView = BannerView.None;
    private string? _recoveryFailureMessage;
    private ElementReference _reloadButtonRef;
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

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_currentView == BannerView.Error && _previousView != BannerView.Error)
        {
            try
            {
                await _reloadButtonRef.FocusAsync();
            }
            catch (JSDisconnectedException) { }
            catch (TaskCanceledException) { }
        }

        _previousView = _currentView;

        await base.OnAfterRenderAsync(firstRender);
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
