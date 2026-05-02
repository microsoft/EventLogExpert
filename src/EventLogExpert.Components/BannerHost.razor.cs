// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace EventLogExpert.Components;

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

    [Inject] private IBannerService BannerService { get; init; } = null!;

    [Inject] private IClipboardService ClipboardService { get; init; } = null!;

    [Inject] private IMenuActionService MenuActionService { get; init; } = null!;

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
        if (_currentView == BannerView.Critical && _previousView != BannerView.Critical)
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

    private static bool ItemMatches(BannerCycleItem selected, BannerCycleItem candidate)
    {
        if (selected.View != candidate.View)
        {
            return false;
        }

        // EntryId is the stable identity for multi-entry slices (Error/Info). For singleton slices
        // (Attention/UpgradeProgress/Critical) both carry null and the View match above is sufficient.
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
            // Cancel delegates close over a CancellationTokenSource; defensive catch so a disposed CTS or other
            // unexpected throw does not bubble into ErrorBoundary and escalate the visible banner to Critical.
            TraceLogger.Error(
                $"{nameof(BannerHost)}.{nameof(OnCancelUpgradeClickedAsync)}: cancel threw: {ex}");
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
        catch (TaskCanceledException) { }
    }

    private void OnCycleNext()
    {
        if (_displayedIndex >= _items.Count - 1){
            return;
        }

        _displayedIndex++;
        _selectedItem = _items[_displayedIndex];
    }

    private void OnCyclePrev()
    {
        if (_displayedIndex <= 0) { return; }

        _displayedIndex--;
        _selectedItem = _items[_displayedIndex];
    }

    private void OnDismissAttention() => BannerService.DismissAttention();

    private void OnDismissError(Guid id) => BannerService.DismissError(id);

    private void OnDismissInfo(Guid id) => BannerService.DismissInfoBanner(id);

    private async Task OnErrorActionClickedAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            // Caller-provided actions own their own error handling; we log and swallow here so a failed action
            // does not bubble to ErrorBoundary and escalate the visible banner from Error to Critical.
            TraceLogger.Error($"{nameof(BannerHost)}.{nameof(OnErrorActionClickedAsync)}: action threw: {ex}");
        }
    }

    private async Task OnOpenSettingsClickedAsync()
    {
        bool success;

        try
        {
            success = await MenuActionService.OpenSettingsAsync();
        }
        catch (JSDisconnectedException)
        {
            // Circuit gone — nothing to render an error into anyway.
            return;
        }
        catch (TaskCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            // Defensive: the IMenuActionService contract says OpenSettingsAsync catches internally and returns
            // bool, but a synchronous throw before the await would still bubble. Swallow so we do not escalate
            // to Critical via ErrorBoundary, and surface a recoverable error so the user knows the click did
            // something. Leave the attention banner up — the underlying databases still need attention.
            TraceLogger.Error($"{nameof(BannerHost)}.{nameof(OnOpenSettingsClickedAsync)}: open settings threw: {ex}");

            Guid errorId = BannerService.ReportError("Settings", $"Failed to open settings: {ex.Message}");
            // Steer selection to the new error so the user actually SEES the failure message instead of being
            // left on the stale Attention selection (ItemMatches preserves Attention by (View, null) otherwise).
            // IndexWithinSlice is a placeholder — RebuildItemsAndPickSelected refreshes it from the new snapshot.
            _selectedItem = new BannerCycleItem(BannerView.Error, 0, errorId);
            return;
        }

        if (success)
        {
            // Dismiss attention only AFTER the modal opened. The FileName-ratchet in DismissAttention
            // suppresses re-raising the banner for the SAME databases when settings closes (typical happy path:
            // user enabled or removed something); NEW databases entering the attention bucket later re-raise.
            BannerService.DismissAttention();
        }
        else
        {
            // OpenSettingsAsync caught internally and returned false. Keep attention banner up so the
            // underlying state stays visible, and report a recoverable error so the user knows the click
            // was received but the modal failed to open.
            TraceLogger.Error($"{nameof(BannerHost)}.{nameof(OnOpenSettingsClickedAsync)}: open settings returned false");

            Guid errorId =BannerService.ReportError("Settings", "Failed to open settings; try again from the menu.");
            // Steer selection to the new error so the user actually SEES the failure message instead of being
            // left on the stale Attention selection (ItemMatches preserves Attention by (View, null) otherwise).
            // IndexWithinSlice is a placeholder — RebuildItemsAndPickSelected refreshes it from the new snapshot.
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

    /// <summary>
    ///     Recompute the cycle list from the latest banner state and pick the displayed index. Preserves the user's
    ///     selection across rebuilds by matching <see cref="BannerCycleItem.View" /> + <see cref="BannerCycleItem.EntryId" />
    ///     (NOT record equality, which would also compare the volatile <see cref="BannerCycleItem.IndexWithinSlice" />
    ///     and silently lose the selection or land on the wrong entry whenever a preceding error/info is dismissed);
    ///     falls back to clamping the previous index when the saved item is no longer active. Always called
    ///     synchronously inside the render path. The caller passes in the snapshots it captured at the top of the
    ///     render block so this method and the razor template index into the SAME snapshot — re-reading
    ///     <see cref="IBannerService" /> properties here would create a window in which the cycle item's
    ///     <see cref="BannerCycleItem.IndexWithinSlice" /> could refer to a different entry than the razor's
    ///     captured snapshot, producing a wrong-entry render.
    /// </summary>
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

        // Preserve the user's selection across rebuilds when possible so a state tick (e.g., upgrade progress)
        // does not jolt their position. Match by (View, EntryId) so dismissals of preceding errors/infos do not
        // shift the user onto a different entry whose IndexWithinSlice happens to match. If the previously
        // selected item is no longer in the list, clamp to the last valid index so dismissals near the end of the
        // cycle do not silently jump to the start.
        if (_selectedItem is not null)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (!ItemMatches(_selectedItem, items[i])) { continue; }

                _displayedIndex = i;
                // Refresh _selectedItem from the new snapshot so its IndexWithinSlice reflects the current
                // slice position (callers index into the captured snapshot using IndexWithinSlice).
                _selectedItem = items[i];
                
                return (_selectedItem, _selectedItem.View);
            }
        }

        _displayedIndex = Math.Clamp(_displayedIndex, 0, items.Count - 1);
        _selectedItem = items[_displayedIndex];
        
        return (_selectedItem, _selectedItem.View);
    }
}
