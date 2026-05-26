// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.Common.Restart;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.Menu;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace EventLogExpert.UI.Banner;

public sealed partial class BannerHost : ComponentBase, IDisposable
{
    private CancellationTokenSource? _copiedFeedbackCts;
    private BannerView _currentView;
    private int _displayedIndex;
    private IReadOnlyList<BannerCycleItem> _items = [];
    private BannerView _previousView = BannerView.None;
    private string? _recoveryFailureMessage;
    private ElementReference _reloadButtonRef;
    private string? _restartFailureMessage;
    private BannerCycleItem? _selectedItem;
    private bool _showCopiedFeedback;

    [Inject] private IApplicationRestartService ApplicationRestartService { get; init; } = null!;

    [Inject] private IAttentionBannerService AttentionBannerService { get; init; } = null!;

    [Inject] private IClipboardService ClipboardService { get; init; } = null!;

    [Inject] private ICriticalErrorService CriticalErrorService { get; init; } = null!;

    [Inject] private IErrorBannerService ErrorBannerService { get; init; } = null!;

    [Inject] private IInfoBannerService InfoBannerService { get; init; } = null!;

    [Inject] private IMenuActionService MenuActionService { get; init; } = null!;

    [Inject] private IProgressBannerService ProgressBannerService { get; init; } = null!;

    [Inject] private ITraceLogger TraceLogger { get; init; } = null!;

    public void Dispose()
    {
        UnsubscribeAll();
        CancellationTokenSource? cts = _copiedFeedbackCts;
        _copiedFeedbackCts = null;
        cts?.Cancel();
        cts?.Dispose();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_currentView == BannerView.Critical && _previousView != BannerView.Critical)
        {
            try
            {
                await _reloadButtonRef.FocusAsync();
            }
            catch (JSDisconnectedException) { /* Circuit gone — nothing to focus. */ }
            catch (TaskCanceledException) { /* Focus cancelled mid-render; harmless. */ }
        }

        _previousView = _currentView;

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override void OnInitialized()
    {
        SubscribeAll();

        base.OnInitialized();
    }

    private static bool ItemMatches(BannerCycleItem selected, BannerCycleItem candidate)
    {
        if (selected.View != candidate.View)
        {
            return false;
        }

        return selected.EntryId == candidate.EntryId;
    }

    private async Task OnCancelUpgradeClickedAsync(BannerProgressEntry entry)
    {
        try
        {
            entry.Cancel();
        }
        catch (Exception ex)
        {
            TraceLogger.Error($"{nameof(BannerHost)}.{nameof(OnCancelUpgradeClickedAsync)}: cancel threw: {ex}");
        }

        await Task.CompletedTask;
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

    private void OnCycleNext()
    {
        if (_displayedIndex >= _items.Count - 1) { return; }

        _displayedIndex++;
        _selectedItem = _items[_displayedIndex];
    }

    private void OnCyclePrev()
    {
        if (_displayedIndex <= 0) { return; }

        _displayedIndex--;
        _selectedItem = _items[_displayedIndex];
    }

    private void OnDismissAttention() => AttentionBannerService.DismissAttention();

    private void OnDismissError(BannerId id) => ErrorBannerService.DismissError(id);

    private void OnDismissInfo(BannerId id) => InfoBannerService.DismissInfoBanner(id);

    private async Task OnErrorActionClickedAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            TraceLogger.Error($"{nameof(BannerHost)}.{nameof(OnErrorActionClickedAsync)}: action threw: {ex}");
        }
    }

    private async Task OnOpenSettingsClickedAsync()
    {
        AttentionBannerService.DismissAttention();

        bool success;

        try
        {
            success = await MenuActionService.OpenSettingsAsync();
        }
        catch (JSDisconnectedException)
        {
            return;
        }
        catch (TaskCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            TraceLogger.Error($"{nameof(BannerHost)}.{nameof(OnOpenSettingsClickedAsync)}: open settings threw: {ex}");

            BannerId errorId = ErrorBannerService.ReportError("Settings", $"Failed to open settings: {ex.Message}");
            _selectedItem = new BannerCycleItem(BannerView.Error, 0, errorId);

            return;
        }

        if (!success)
        {
            TraceLogger.Error($"{nameof(BannerHost)}.{nameof(OnOpenSettingsClickedAsync)}: open settings returned false");

            BannerId errorId = ErrorBannerService.ReportError("Settings", "Failed to open settings; try again from the menu.");
            _selectedItem = new BannerCycleItem(BannerView.Error, 0, errorId);
        }
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

            TraceLogger.Error($"{nameof(BannerHost)}.{nameof(OnReloadClickedAsync)}: recovery threw: {ex}");

            StateHasChanged();
        }
    }

    private void OnStateChanged() => _ = InvokeAsync(StateHasChanged);

    private (BannerCycleItem? Selected, BannerView View) RebuildItemsAndPickSelected(
        Exception? currentCritical,
        IReadOnlyList<ErrorBannerEntry> errors,
        IReadOnlyList<DatabaseEntry> attentionEntries,
        bool attentionDismissed,
        BannerProgressEntry? backgroundProgress,
        IReadOnlyList<BannerInfoEntry> infos)
    {
        IReadOnlyList<BannerCycleItem> items = BannerViewSelector.BuildCycle(
            currentCritical,
            errors,
            attentionEntries,
            attentionDismissed,
            backgroundProgress,
            infos);

        _items = items;

        if (items.Count == 0)
        {
            _selectedItem = null;
            _displayedIndex = 0;
            return (null, BannerView.None);
        }

        if (_selectedItem is not null)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (!ItemMatches(_selectedItem, items[i])) { continue; }

                _displayedIndex = i;
                _selectedItem = items[i];

                return (_selectedItem, _selectedItem.View);
            }
        }

        _displayedIndex = Math.Clamp(_displayedIndex, 0, items.Count - 1);
        _selectedItem = items[_displayedIndex];

        return (_selectedItem, _selectedItem.View);
    }

    private void SubscribeAll()
    {
        AttentionBannerService.StateChanged += OnStateChanged;
        ProgressBannerService.StateChanged += OnStateChanged;
        CriticalErrorService.StateChanged += OnStateChanged;
        ErrorBannerService.StateChanged += OnStateChanged;
        InfoBannerService.StateChanged += OnStateChanged;
    }

    private void UnsubscribeAll()
    {
        AttentionBannerService.StateChanged -= OnStateChanged;
        ProgressBannerService.StateChanged -= OnStateChanged;
        CriticalErrorService.StateChanged -= OnStateChanged;
        ErrorBannerService.StateChanged -= OnStateChanged;
        InfoBannerService.StateChanged -= OnStateChanged;
    }
}
