// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.Modal;
using EventLogExpert.UI.DatabaseTools;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.Banner;

public sealed partial class BannerHost : ComponentBase, IDisposable
{
    private BannerView _currentView;
    private int _displayedIndex;
    private IReadOnlyList<BannerCycleItem> _items = [];
    private BannerCycleItem? _selectedItem;

    [Inject] private IAttentionBannerService AttentionBannerService { get; init; } = null!;

    [Inject] private ICriticalErrorService CriticalErrorService { get; init; } = null!;

    [Inject] private IErrorBannerService ErrorBannerService { get; init; } = null!;

    [Inject] private IInfoBannerService InfoBannerService { get; init; } = null!;

    [Inject] private IModalCoordinator ModalCoordinator { get; init; } = null!;

    [Inject] private IProgressBannerService ProgressBannerService { get; init; } = null!;

    public void Dispose() => UnsubscribeAll();

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

    private void HandleFallbackErrorPosted(BannerCycleItem newCycleItem) =>
        _selectedItem = newCycleItem;

    private bool IsAttentionSuppressedByModalContext() =>
        ModalCoordinator.ActiveSession?.ComponentType == typeof(DatabaseToolsModal);

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

    private void OnStateChanged() => _ = InvokeAsync(StateHasChanged);

    private (BannerCycleItem? Selected, BannerView View) RebuildItemsAndPickSelected(
        Exception? currentCritical,
        IReadOnlyList<ErrorBannerEntry> errors,
        IReadOnlyList<DatabaseEntry> attentionEntries,
        bool attentionDismissed,
        bool attentionSuppressedByModalContext,
        BannerProgressEntry? backgroundProgress,
        IReadOnlyList<BannerInfoEntry> infos)
    {
        IReadOnlyList<BannerCycleItem> items = BannerViewSelector.BuildCycle(
            currentCritical,
            errors,
            attentionEntries,
            attentionDismissed,
            attentionSuppressedByModalContext,
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
        ModalCoordinator.StateChanged += OnStateChanged;
    }

    private void UnsubscribeAll()
    {
        AttentionBannerService.StateChanged -= OnStateChanged;
        ProgressBannerService.StateChanged -= OnStateChanged;
        CriticalErrorService.StateChanged -= OnStateChanged;
        ErrorBannerService.StateChanged -= OnStateChanged;
        InfoBannerService.StateChanged -= OnStateChanged;
        ModalCoordinator.StateChanged -= OnStateChanged;
    }
}
