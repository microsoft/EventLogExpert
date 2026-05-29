// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Modal;
using EventLogExpert.UI.DatabaseTools;

namespace EventLogExpert.UI.Banner;

public sealed class BannerCycleStateService : IBannerCycleStateService, IDisposable
{
    private readonly IAttentionBannerService _attention;
    private readonly ICriticalErrorService _critical;
    private readonly IErrorBannerService _errors;
    private readonly IInfoBannerService _infos;
    private readonly IModalCoordinator _modalCoordinator;
    private readonly IProgressBannerService _progress;
    private readonly Lock _stateLock = new();

    private BannerView _currentView;
    private int _displayedIndex;
    private IReadOnlyList<BannerCycleItem> _items = [];
    private bool _modalContentDisplayed;
    private BannerCycleItem? _pendingOverrideItem;
    private BannerCycleItem? _selectedItem;

    public BannerCycleStateService(
        IAttentionBannerService attention,
        IErrorBannerService errors,
        IInfoBannerService infos,
        IProgressBannerService progress,
        ICriticalErrorService critical,
        IModalCoordinator modalCoordinator)
    {
        _attention = attention;
        _errors = errors;
        _infos = infos;
        _progress = progress;
        _critical = critical;
        _modalCoordinator = modalCoordinator;

        _attention.StateChanged += OnFacetChanged;
        _errors.StateChanged += OnFacetChanged;
        _infos.StateChanged += OnFacetChanged;
        _progress.StateChanged += OnFacetChanged;
        _critical.StateChanged += OnFacetChanged;
        _modalCoordinator.StateChanged += OnFacetChanged;

        RebuildAndReselect();
    }

    public event Action? StateChanged;

    public BannerView CurrentView
    {
        get { using (_stateLock.EnterScope()) { return _currentView; } }
    }

    public int DisplayedIndex
    {
        get { using (_stateLock.EnterScope()) { return _displayedIndex; } }
    }

    public IReadOnlyList<BannerCycleItem> Items
    {
        get { using (_stateLock.EnterScope()) { return _items; } }
    }

    public bool ModalContentDisplayed
    {
        get { using (_stateLock.EnterScope()) { return _modalContentDisplayed; } }
    }

    public BannerCycleItem? SelectedItem
    {
        get { using (_stateLock.EnterScope()) { return _selectedItem; } }
    }

    public void Dispose()
    {
        _attention.StateChanged -= OnFacetChanged;
        _errors.StateChanged -= OnFacetChanged;
        _infos.StateChanged -= OnFacetChanged;
        _progress.StateChanged -= OnFacetChanged;
        _critical.StateChanged -= OnFacetChanged;
        _modalCoordinator.StateChanged -= OnFacetChanged;
    }

    public void MoveNext()
    {
        using (_stateLock.EnterScope())
        {
            var items = _items;
            if (_displayedIndex >= items.Count - 1) { return; }

            _displayedIndex++;
            _selectedItem = items[_displayedIndex];
            _currentView = _selectedItem.View;
        }

        StateChanged?.Invoke();
    }

    public void MovePrev()
    {
        using (_stateLock.EnterScope())
        {
            var items = _items;
            if (_displayedIndex <= 0 || items.Count == 0) { return; }

            _displayedIndex--;
            _selectedItem = items[_displayedIndex];
            _currentView = _selectedItem.View;
        }

        StateChanged?.Invoke();
    }

    public void RegisterFallbackError(BannerCycleItem newCycleItem)
    {
        using (_stateLock.EnterScope())
        {
            _pendingOverrideItem = newCycleItem;
            RebuildAndReselectLocked();
        }

        StateChanged?.Invoke();
    }

    public void SetModalContentDisplayed(bool displayed)
    {
        using (_stateLock.EnterScope())
        {
            if (_modalContentDisplayed == displayed) { return; }

            _modalContentDisplayed = displayed;
        }

        StateChanged?.Invoke();
    }

    private static bool ItemMatches(BannerCycleItem selected, BannerCycleItem candidate)
    {
        if (selected.View != candidate.View) { return false; }

        return selected.EntryId == candidate.EntryId;
    }

    private void OnFacetChanged()
    {
        using (_stateLock.EnterScope())
        {
            RebuildAndReselectLocked();
        }

        StateChanged?.Invoke();
    }

    private void RebuildAndReselect()
    {
        using (_stateLock.EnterScope())
        {
            RebuildAndReselectLocked();
        }
    }

    private void RebuildAndReselectLocked()
    {
        if (_modalCoordinator.ActiveSession is null)
        {
            _modalContentDisplayed = false;
        }

        bool attentionSuppressed =
            _modalCoordinator.ActiveSession?.ComponentType == typeof(DatabaseToolsModal);

        IReadOnlyList<BannerCycleItem> items = BannerViewSelector.BuildCycle(
            _critical.CurrentCritical,
            _errors.ErrorBanners,
            _attention.AttentionEntries,
            _attention.AttentionDismissed,
            attentionSuppressed,
            _progress.BackgroundProgress,
            _infos.InfoBanners);

        _items = items;

        if (items.Count == 0)
        {
            _selectedItem = null;
            _displayedIndex = 0;
            _currentView = BannerView.None;
            _pendingOverrideItem = null;
            return;
        }

        if (_pendingOverrideItem is not null)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (!ItemMatches(_pendingOverrideItem, items[i])) { continue; }

                _displayedIndex = i;
                _selectedItem = items[i];
                _currentView = _selectedItem.View;
                _pendingOverrideItem = null;
                return;
            }

            _pendingOverrideItem = null;
        }

        if (_selectedItem is not null)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (!ItemMatches(_selectedItem, items[i])) { continue; }

                _displayedIndex = i;
                _selectedItem = items[i];
                _currentView = _selectedItem.View;
                return;
            }
        }

        _displayedIndex = Math.Clamp(_displayedIndex, 0, items.Count - 1);
        _selectedItem = items[_displayedIndex];
        _currentView = _selectedItem.View;
    }
}
