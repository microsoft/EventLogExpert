// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Database;
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
    // _priorityStolenSelection holds the most recent item that won selection via priorityOverride
    // (i.e., a newly-arrived higher-priority item). Cleared (a) when its source identity is no longer
    // in the current source fingerprint, or (b) when the user explicitly navigates via MoveNext/MovePrev
    // (user-acknowledgment). While non-null, the _userPreferredItem restore path is gated off so a
    // stale low-priority preference cannot bounce back over the priority-stolen selection on the next
    // unrelated rebuild.
    private BannerCycleItem? _priorityStolenSelection;
    private HashSet<(BannerView View, BannerId? EntryId)> _priorSourceFingerprint = [];
    private BannerCycleItem? _selectedItem;
    // _userPreferredItem captures the last item the user explicitly selected via MoveNext/MovePrev.
    // It survives temporary source-filter events (e.g., the DatabaseToolsModal suppressing the
    // Attention banner) so the user's preferred banner is restored when the filter lifts.
    private BannerCycleItem? _userPreferredItem;

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
            _userPreferredItem = _selectedItem;
            _priorityStolenSelection = null;
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
            _userPreferredItem = _selectedItem;
            _priorityStolenSelection = null;
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

    private static BannerCycleItem? ComputePriorityOverride(
        IReadOnlyList<BannerCycleItem> items,
        HashSet<(BannerView View, BannerId? EntryId)> priorFingerprint,
        BannerCycleItem? currentSelection)
    {
        int currentRank = currentSelection is null ? 0 : PriorityRank(currentSelection.View);
        BannerCycleItem? highest = null;
        int highestRank = currentRank;

        foreach (var item in items)
        {
            if (priorFingerprint.Contains((item.View, item.EntryId))) { continue; }

            int rank = PriorityRank(item.View);
            if (rank > highestRank)
            {
                highest = item;
                highestRank = rank;
            }
        }

        return highest;
    }

    private static HashSet<(BannerView View, BannerId? EntryId)> ComputeSourceFingerprintFromSnapshot(
        Exception? critical,
        IReadOnlyList<ErrorBannerEntry> errors,
        IReadOnlyList<DatabaseEntry> attentionEntries,
        BannerProgressEntry? backgroundProgress,
        IReadOnlyList<BannerInfoEntry> infos)
    {
        var result = new HashSet<(BannerView View, BannerId? EntryId)>();

        if (critical is not null) { result.Add((BannerView.Critical, null)); }

        foreach (var err in errors) { result.Add((BannerView.Error, err.Id)); }

        if (attentionEntries.Count > 0) { result.Add((BannerView.Attention, null)); }

        if (backgroundProgress is not null) { result.Add((BannerView.UpgradeProgress, null)); }

        foreach (var info in infos) { result.Add((BannerView.Info, info.Id)); }

        return result;
    }

    private static bool ItemMatches(BannerCycleItem selected, BannerCycleItem candidate)
    {
        if (selected.View != candidate.View) { return false; }

        return selected.EntryId == candidate.EntryId;
    }

    // BannerView enum is ordered for display, not priority — use this rank for comparisons.
    private static int PriorityRank(BannerView view) => view switch
    {
        BannerView.Critical => 5,
        BannerView.Error => 4,
        BannerView.Attention => 3,
        BannerView.UpgradeProgress => 2,
        BannerView.Info => 1,
        _ => 0
    };

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

        // Capture each facet snapshot once — the same data drives BuildCycle AND the fingerprint.
        var critical = _critical.CurrentCritical;
        var errors = _errors.ErrorBanners;
        var attentionEntries = _attention.AttentionEntries;
        var attentionDismissed = _attention.AttentionDismissed;
        var backgroundProgress = _progress.BackgroundProgress;
        var infos = _infos.InfoBanners;

        IReadOnlyList<BannerCycleItem> items = BannerViewSelector.BuildCycle(
            critical,
            errors,
            attentionEntries,
            attentionDismissed,
            attentionSuppressed,
            backgroundProgress,
            infos);

        // Fingerprint is built from the UNFILTERED source — otherwise modal close would re-introduce
        // Attention and wrongly trigger priorityOverride on every reentry.
        var currentSourceFingerprint = ComputeSourceFingerprintFromSnapshot(
            critical,
            errors,
            attentionEntries,
            backgroundProgress,
            infos);

        BannerCycleItem? priorityOverride = ComputePriorityOverride(
            items,
            _priorSourceFingerprint,
            _selectedItem);

        // Advance fingerprint before any early-return so the next rebuild compares against current state.
        _priorSourceFingerprint = currentSourceFingerprint;

        if (_userPreferredItem is not null
            && !currentSourceFingerprint.Contains((_userPreferredItem.View, _userPreferredItem.EntryId)))
        {
            _userPreferredItem = null;
        }

        if (_priorityStolenSelection is not null
            && !currentSourceFingerprint.Contains(
                (_priorityStolenSelection.View, _priorityStolenSelection.EntryId)))
        {
            _priorityStolenSelection = null;
        }

        _items = items;

        if (items.Count == 0)
        {
            _selectedItem = null;
            _displayedIndex = 0;
            _currentView = BannerView.None;
            _pendingOverrideItem = null;
            return;
        }

        // Priority order: explicit override > priority watermark > user-preferred (gated) > current > clamp.
        if (TryApplySelection(_pendingOverrideItem, items)) { _pendingOverrideItem = null; return; }

        if (TryApplySelection(priorityOverride, items))
        {
            _priorityStolenSelection = priorityOverride;
            return;
        }

        // Gate user-preferred restore against a still-active priority-stolen item only. This does NOT
        // block restore when a higher-priority item that COEXISTED with the user's preference is
        // present — only blocks against a newly-arrived steal that is still in the cycle.
        if (_priorityStolenSelection is null
            && TryApplySelection(_userPreferredItem, items))
        {
            return;
        }

        if (TryApplySelection(_selectedItem, items)) { return; }

        _displayedIndex = Math.Clamp(_displayedIndex, 0, items.Count - 1);
        _selectedItem = items[_displayedIndex];
        _currentView = _selectedItem.View;
    }

    private bool TryApplySelection(BannerCycleItem? target, IReadOnlyList<BannerCycleItem> items)
    {
        if (target is null) { return false; }

        for (int i = 0; i < items.Count; i++)
        {
            if (!ItemMatches(target, items[i])) { continue; }

            _displayedIndex = i;
            _selectedItem = items[i];
            _currentView = _selectedItem.View;
            return true;
        }

        return false;
    }
}
