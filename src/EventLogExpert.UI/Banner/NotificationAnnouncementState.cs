// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.UI.Alerts;
using EventLogExpert.UI.Common;
using EventLogExpert.UI.Modal;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.Banner;

internal sealed class NotificationAnnouncementState : IDisposable
{
    private static readonly TimeSpan s_toastBurstDuration = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan s_toastCoalesceWindow = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan s_toastDuration = TimeSpan.FromSeconds(6);

    private readonly Dictionary<string, string> _announcedSignatures = new(StringComparer.Ordinal);
    private readonly IAttentionBannerService _attentionBannerService;
    private readonly ICriticalErrorService _criticalErrorService;
    private readonly IErrorBannerService _errorBannerService;
    private readonly IExportProgressBannerService _exportProgressBannerService;
    private readonly Dictionary<object, HostState> _hosts = [];
    private readonly IInfoBannerService _infoBannerService;
    private readonly IStringLocalizer<SharedResource> _localizer;
    private readonly IModalCoordinator _modalCoordinator;
    private readonly IProgressBannerService _progressBannerService;
    private readonly Lock _stateLock = new();
    private readonly TimeProvider _timeProvider;

    private object? _activeHost;
    private object? _announcementHost;
    private string? _assertiveAnnouncement;
    private long _attentionSequence;
    private bool _attentionWasActive;
    private string? _backgroundProgressIdentity;
    private DateTime _backgroundProgressStartedUtc;
    private bool _clearInProgress;
    private bool _disposed;
    private long _exportProgressSequence;
    private DateTime _exportProgressStartedUtc;
    private bool _hasDeferredFailure;
    private bool _hasDeferredPoliteNotice;
    private bool _isPanelOpen;
    private Dictionary<string, Notice> _knownNotices;
    private ExportProgressEntry? _lastExportProgressEntry;
    private string? _pendingClearAnnouncement;
    private string? _politeAnnouncement;
    private Notice? _politeNoticeDuringClear;
    private NotificationToast? _toast;
    private int _toastArrivalCount;
    private DateTimeOffset _toastBurstStartedUtc;
    private DateTimeOffset _toastCoalesceEndsUtc;
    private DateTimeOffset _toastDeadlineUtc;
    private long _toastGeneration;
    private Notice? _toastNotice;
    private ITimer? _toastTimer;
    private bool _wasDeferred;

    public NotificationAnnouncementState(
        IAttentionBannerService attentionBannerService,
        ICriticalErrorService criticalErrorService,
        IErrorBannerService errorBannerService,
        IExportProgressBannerService exportProgressBannerService,
        IInfoBannerService infoBannerService,
        IModalCoordinator modalCoordinator,
        IProgressBannerService progressBannerService,
        IStringLocalizer<SharedResource> localizer,
        TimeProvider? timeProvider = null)
    {
        _attentionBannerService = attentionBannerService;
        _criticalErrorService = criticalErrorService;
        _errorBannerService = errorBannerService;
        _exportProgressBannerService = exportProgressBannerService;
        _infoBannerService = infoBannerService;
        _modalCoordinator = modalCoordinator;
        _progressBannerService = progressBannerService;
        _localizer = localizer;
        _timeProvider = timeProvider ?? TimeProvider.System;

        lock (_stateLock)
        {
            _knownNotices = CreateCurrentNotices().ToDictionary(notice => notice.Identity, StringComparer.Ordinal);
        }

        _attentionBannerService.StateChanged += OnSourceChanged;
        _criticalErrorService.StateChanged += OnSourceChanged;
        _errorBannerService.StateChanged += OnSourceChanged;
        _exportProgressBannerService.StateChanged += OnSourceChanged;
        _infoBannerService.StateChanged += OnSourceChanged;
        _modalCoordinator.StateChanged += OnSourceChanged;
        _progressBannerService.StateChanged += OnSourceChanged;

        OnSourceChanged();
    }

    public event Action? StateChanged;

    public void AnnounceCleared(int removedCount)
    {
        lock (_stateLock)
        {
            string announcement = PluralText.Format(_localizer, "Notifications_Cleared", ("count", removedCount));
            int runningCount = (_progressBannerService.BackgroundProgress is not null ? 1 : 0)
                + (_exportProgressBannerService.CurrentExport is not null ? 1 : 0);

            if (runningCount > 0)
            {
                announcement += PluralText.Format(_localizer, "Notifications_TasksRunning", ("count", runningCount));
            }

            _pendingClearAnnouncement = announcement;
            UpdateActiveHostLocked();
        }
        StateChanged?.Invoke();
    }

    public void BeginClear()
    {
        lock (_stateLock)
        {
            _clearInProgress = true;
            _politeNoticeDuringClear = null;
        }
    }

    public void Dispose()
    {
        _attentionBannerService.StateChanged -= OnSourceChanged;
        _criticalErrorService.StateChanged -= OnSourceChanged;
        _errorBannerService.StateChanged -= OnSourceChanged;
        _exportProgressBannerService.StateChanged -= OnSourceChanged;
        _infoBannerService.StateChanged -= OnSourceChanged;
        _modalCoordinator.StateChanged -= OnSourceChanged;
        _progressBannerService.StateChanged -= OnSourceChanged;

        lock (_stateLock)
        {
            _disposed = true;
            _toastTimer?.Dispose();
            _toastTimer = null;
            _toast = null;
            _toastNotice = null;
            _hosts.Clear();
            _activeHost = null;
            _announcementHost = null;
        }
    }

    public void EndClear()
    {
        lock (_stateLock)
        {
            _clearInProgress = false;

            if (_pendingClearAnnouncement is null) { _politeNoticeDuringClear = null; }
        }
    }

    public (string? Polite, string? Assertive) GetAnnouncements(object host)
    {
        lock (_stateLock)
        {
            return ReferenceEquals(_announcementHost, host) ?
                (_politeAnnouncement, _assertiveAnnouncement) :
                (null, null);
        }
    }

    public NotificationToast? GetToast(object host)
    {
        lock (_stateLock)
        {
            return IsToastAvailableLocked(host) ? _toast : null;
        }
    }

    public bool IsActiveHost(object host)
    {
        lock (_stateLock) { return ReferenceEquals(_activeHost, host); }
    }

    public void SetHostAvailability(object host, bool isAvailable, bool isModal)
    {
        ArgumentNullException.ThrowIfNull(host);
        bool changed;

        lock (_stateLock)
        {
            _hosts[host] = new HostState(isAvailable, isModal);
            changed = UpdateActiveHostLocked();
        }

        if (changed) { StateChanged?.Invoke(); }
    }

    public void SetPanelOpen(object host, bool isOpen)
    {
        bool changed;

        lock (_stateLock)
        {
            if (!_hosts.TryGetValue(host, out HostState? hostState) || hostState.IsModal)
            {
                return;
            }

            changed = _isPanelOpen != isOpen;
            _isPanelOpen = isOpen;

            if (isOpen)
            {
                changed |= ClearToastLocked();
            }
        }

        if (changed) { StateChanged?.Invoke(); }
    }

    public void UnregisterHost(object host)
    {
        bool changed;

        lock (_stateLock)
        {
            _hosts.Remove(host);
            changed = UpdateActiveHostLocked();
        }

        if (changed) { StateChanged?.Invoke(); }
    }

    private static int GetSeverityRank(NotificationSeverity severity) => severity switch
    {
        NotificationSeverity.Error => 3,
        NotificationSeverity.Warning => 2,
        _ => 1
    };

    private static string GetSignature(Notice notice) => notice.MaterialSignature;

    private static DateTimeOffset Min(DateTimeOffset first, DateTimeOffset second) => first <= second ? first : second;

    // Live regions do not re-announce unchanged text; screen readers do not pronounce the zero-width space.
    private static string WithRepeatMarker(string announcement, string? previous) =>
        previous == announcement ? announcement + "\u200B" : announcement;

    private bool ClearToastLocked()
    {
        if (_toast is null && _toastTimer is null) { return false; }

        _toastGeneration++;
        _toastTimer?.Dispose();
        _toastTimer = null;
        _toast = null;
        _toastNotice = null;
        _toastArrivalCount = 0;
        return true;
    }

    private IReadOnlyList<Notice> CreateCurrentNotices()
    {
        List<Notice> notices = [];

        foreach (ErrorBannerEntry entry in _errorBannerService.ErrorBanners)
        {
            BannerContentText text = BannerContentLocalizer.Resolve(_localizer, entry.Content);
            notices.Add(new Notice(
                $"error:{entry.Id}",
                NotificationSeverity.Error,
                text.Title,
                text.Message,
                entry.CreatedUtc,
                entry.Content.ToString() ?? string.Empty,
                true));
        }

        foreach (BannerInfoEntry entry in _infoBannerService.InfoBanners)
        {
            BannerContentText text = BannerContentLocalizer.Resolve(_localizer, entry.Content);
            NotificationSeverity severity = entry.Severity == BannerSeverity.Warning ?
                NotificationSeverity.Warning :
                NotificationSeverity.Info;

            notices.Add(new Notice(
                $"info:{entry.Id}",
                severity,
                text.Title,
                text.Message,
                entry.CreatedUtc,
                $"{entry.Severity}:{entry.Content}",
                true));
        }

        int attentionCount = _attentionBannerService.AttentionEntries.Count;
        bool hasAttention = attentionCount > 0 && !_attentionBannerService.AttentionDismissed;

        if (hasAttention)
        {
            if (!_attentionWasActive) { _attentionSequence++; }

            _attentionWasActive = true;
            BannerContentText text = BannerContentLocalizer.Resolve(_localizer, new DatabaseAttention(attentionCount));

            notices.Add(new Notice(
                $"attention:{_attentionSequence}",
                NotificationSeverity.Warning,
                text.Title,
                text.Message,
                _attentionBannerService.AttentionCreatedUtc,
                new DatabaseAttention(attentionCount).ToString() ?? string.Empty,
                true));
        }
        else
        {
            _attentionWasActive = false;
        }

        BannerProgressEntry? backgroundProgress = _progressBannerService.BackgroundProgress;

        if (backgroundProgress is not null)
        {
            string identity = $"upgrade:{backgroundProgress.BatchId}";

            if (_backgroundProgressIdentity != identity)
            {
                _backgroundProgressIdentity = identity;
                _backgroundProgressStartedUtc = _timeProvider.GetUtcNow().UtcDateTime;
            }

            notices.Add(new Notice(
                identity,
                NotificationSeverity.Progress,
                _localizer["Banner_Upgrade_Title"],
                _localizer["Notifications_UpgradeStarted"],
                _backgroundProgressStartedUtc,
                identity,
                false));
        }
        else
        {
            _backgroundProgressIdentity = null;
        }

        ExportProgressEntry? exportProgress = _exportProgressBannerService.CurrentExport;

        if (exportProgress is null)
        {
            _lastExportProgressEntry = null;

            return notices;
        }

        if (!ReferenceEquals(_lastExportProgressEntry, exportProgress))
        {
            _lastExportProgressEntry = exportProgress;
            _exportProgressSequence++;
            _exportProgressStartedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        }

        notices.Add(new Notice(
            $"export:{_exportProgressSequence}",
            NotificationSeverity.Progress,
            _localizer["Banner_Export_Title"],
            _localizer["Notifications_ExportStarted"],
            _exportProgressStartedUtc,
            $"export:{_exportProgressSequence}",
            false));

        return notices;
    }

    private string FormatNoticeAnnouncement(Notice notice)
    {
        string severityText = notice.Severity switch
        {
            NotificationSeverity.Progress => _localizer["Notifications_Severity_Progress"].Value,
            NotificationSeverity.Info => _localizer["Notifications_Severity_Info"].Value,
            NotificationSeverity.Warning => _localizer["Notifications_Severity_Warning"].Value,
            NotificationSeverity.Error => _localizer["Notifications_Severity_Error"].Value,
            _ => throw new ArgumentOutOfRangeException(nameof(notice.Severity), notice.Severity, "Unknown notification severity.")
        };

        return _localizer["Notifications_NoticeAnnouncement",
            severityText.TrimEnd('.', ' '),
            notice.Title.TrimEnd('.', ' '),
            notice.Message.TrimEnd('.', ' '),
            _localizer["Notifications_ActiveCount", GetActiveCount()].Value].Value;
    }

    private int GetActiveCount()
    {
        int attentionCount =
            _attentionBannerService.AttentionEntries.Count > 0 && !_attentionBannerService.AttentionDismissed ? 1 : 0;

        return _errorBannerService.ErrorBanners.Count +
            _infoBannerService.InfoBanners.Count +
            attentionCount +
            (_progressBannerService.BackgroundProgress is not null ? 1 : 0) +
            (_exportProgressBannerService.CurrentExport is not null ? 1 : 0);
    }

    private bool IsDeferred()
    {
        if (_criticalErrorService.CurrentCritical is not null) { return true; }

        Type? activeComponentType = _modalCoordinator.ActiveSession?.ComponentType;
        return activeComponentType == typeof(AlertModal) || activeComponentType == typeof(PromptModal);
    }

    private bool IsToastAvailableLocked(object host) =>
        !_disposed &&
        !_isPanelOpen &&
        _toast is not null &&
        _criticalErrorService.CurrentCritical is null &&
        _modalCoordinator.ActiveSession is null &&
        _activeHost is not null &&
        ReferenceEquals(_activeHost, host) &&
        _hosts.TryGetValue(host, out HostState? hostState) &&
        hostState is { IsAvailable: true, IsModal: false };

    private bool IsUnannounced(Notice notice) =>
        !_announcedSignatures.TryGetValue(notice.Identity, out string? signature) || signature != GetSignature(notice);

    private void OnSourceChanged()
    {
        bool changed;

        lock (_stateLock)
        {
            IReadOnlyList<Notice> currentNotices = CreateCurrentNotices();
            Dictionary<string, Notice> currentByIdentity = currentNotices.ToDictionary(
                notice => notice.Identity,
                StringComparer.Ordinal);
            bool sourceStateChanged = _knownNotices.Count != currentByIdentity.Count
                || _knownNotices.Any(pair => !currentByIdentity.TryGetValue(pair.Key, out Notice? current)
                    || GetSignature(pair.Value) != GetSignature(current));
            Notice[] newNotices =
            [
                .. currentNotices
                    .Where(notice =>
                        !_knownNotices.TryGetValue(notice.Identity, out Notice? previous) ||
                        GetSignature(previous) != GetSignature(notice))
                    .OrderBy(notice => notice.CreatedUtc)
            ];
            _knownNotices = currentByIdentity;

            foreach (string identity in _announcedSignatures.Keys
                .Where(identity => !currentByIdentity.ContainsKey(identity))
                .ToArray())
            {
                _announcedSignatures.Remove(identity);
            }

            changed = sourceStateChanged;
            changed |= UpdateActiveHostLocked();
            changed |= ProcessNoticesLocked(newNotices);
            changed |= UpdateToastLocked(newNotices);
        }

        if (changed) { StateChanged?.Invoke(); }
    }

    private void OnToastTimerElapsed(long generation)
    {
        bool changed = false;

        lock (_stateLock)
        {
            if (_disposed || generation != _toastGeneration || _toast is null) { return; }

            _toastTimer?.Dispose();
            _toastTimer = null;
            _toast = null;
            _toastNotice = null;
            _toastArrivalCount = 0;
            changed = true;
        }

        if (changed) { StateChanged?.Invoke(); }
    }

    private void ProcessDeferredNoticesLocked(IReadOnlyList<Notice> newNotices)
    {
        _wasDeferred = true;
        foreach (Notice notice in newNotices)
        {
            if (notice.Severity == NotificationSeverity.Error)
            {
                _hasDeferredFailure = true;
            }
            else
            {
                _hasDeferredPoliteNotice = true;
            }
        }
    }

    private bool ProcessNoticesLocked(IReadOnlyList<Notice> newNotices)
    {
        if (IsDeferred() || _activeHost is null)
        {
            ProcessDeferredNoticesLocked(newNotices);

            return false;
        }

        if (_wasDeferred)
        {
            _wasDeferred = false;

            return PublishDeferredLocked();
        }

        Notice? latestFailure = newNotices.LastOrDefault(notice =>
            notice.Severity == NotificationSeverity.Error && IsUnannounced(notice));
        Notice? latestPoliteNotice = newNotices.LastOrDefault(notice =>
            notice.Severity != NotificationSeverity.Error && IsUnannounced(notice));

        if (latestFailure is null && latestPoliteNotice is null) { return false; }

        if (latestFailure is not null) { PublishLocked(latestFailure); }
        
        if (latestPoliteNotice is not null) { PublishLocked(latestPoliteNotice); }
        
        return true;
    }

    private bool PublishDeferredLocked()
    {
        if (_hasDeferredFailure)
        {
            _hasDeferredFailure = false;
            Notice? latestFailure = CreateCurrentNotices()
                .Where(notice => notice.Severity == NotificationSeverity.Error && IsUnannounced(notice))
                .Reverse()
                .MaxBy(notice => notice.CreatedUtc);

            if (latestFailure is not null)
            {
                _hasDeferredPoliteNotice = false;
                PublishLocked(latestFailure);
                return true;
            }
        }

        if (!_hasDeferredPoliteNotice) { return false; }

        _hasDeferredPoliteNotice = false;

        Notice? latestNotice = CreateCurrentNotices()
            .Where(notice => notice.Severity != NotificationSeverity.Error && IsUnannounced(notice))
            .Reverse()
            .MaxBy(notice => notice.CreatedUtc);

        if (latestNotice is not null)
        {
            PublishLocked(latestNotice);

            return true;
        }

        return false;
    }

    private void PublishLocked(Notice notice)
    {
        string announcement = FormatNoticeAnnouncement(notice);

        if (notice.Severity == NotificationSeverity.Error)
        {
            _assertiveAnnouncement = WithRepeatMarker(announcement, _assertiveAnnouncement);
            _politeNoticeDuringClear = null;
        }
        else
        {
            _politeAnnouncement = WithRepeatMarker(announcement, _politeAnnouncement);

            if (_clearInProgress || _pendingClearAnnouncement is not null)
            {
                _politeNoticeDuringClear = notice;
            }
        }

        _announcementHost = _activeHost;
        _announcedSignatures[notice.Identity] = GetSignature(notice);
    }

    private void ScheduleToastTimerLocked(DateTimeOffset now)
    {
        _toastGeneration++;
        long generation = _toastGeneration;
        _toastTimer?.Dispose();
        _toastTimer = _timeProvider.CreateTimer(
            _ => OnToastTimerElapsed(generation),
            null,
            _toastDeadlineUtc - now,
            Timeout.InfiniteTimeSpan);
    }

    private bool UpdateActiveHostLocked()
    {
        object? nextHost = null;
        bool nextIsModal = false;

        foreach ((object host, HostState state) in _hosts)
        {
            if (!state.IsAvailable || IsDeferred()) { continue; }
            
            if (nextHost is not null && (!state.IsModal || nextIsModal)) { continue; }

            nextHost = host;
            nextIsModal = state.IsModal;
        }

        bool changed = !ReferenceEquals(_activeHost, nextHost);
        _activeHost = nextHost;

        if (changed)
        {
            _announcementHost = null;
            _politeAnnouncement = null;
            _assertiveAnnouncement = null;
        }

        if (_activeHost is null || IsDeferred())
        {
            _wasDeferred = true;

            return changed;
        }

        if (_wasDeferred)
        {
            _wasDeferred = false;
            changed |= PublishDeferredLocked();
        }

        if (_pendingClearAnnouncement is { } clearAnnouncement)
        {
            string? politeNotice = _politeNoticeDuringClear is { } noticeDuringClear &&
                _knownNotices.TryGetValue(noticeDuringClear.Identity, out Notice? currentNotice) &&
                GetSignature(currentNotice) == GetSignature(noticeDuringClear) ?
                FormatNoticeAnnouncement(currentNotice) : null;

            string combinedAnnouncement = politeNotice is null ?
                clearAnnouncement : string.Join(" ", clearAnnouncement, politeNotice);

            _politeAnnouncement = WithRepeatMarker(combinedAnnouncement, _politeAnnouncement);
            _announcementHost = _activeHost;
            _pendingClearAnnouncement = null;
            _politeNoticeDuringClear = null;
            changed = true;
        }

        return changed;
    }

    private bool UpdateToastLocked(IReadOnlyList<Notice> newNotices)
    {
        if (_criticalErrorService.CurrentCritical is not null || _modalCoordinator.ActiveSession is not null)
        {
            return ClearToastLocked();
        }

        if (_isPanelOpen ||
            _activeHost is null ||
            !_hosts.TryGetValue(_activeHost, out HostState? activeHost) ||
            !activeHost.IsAvailable ||
            activeHost.IsModal)
        {
            return false;
        }

        bool changed = false;
        DateTimeOffset now = _timeProvider.GetUtcNow();

        foreach (Notice notice in newNotices.Where(notice => notice.IsToastEligible))
        {
            if (_toast is null || now >= _toastDeadlineUtc || now - _toastBurstStartedUtc >= s_toastBurstDuration)
            {
                _toastBurstStartedUtc = now;
                _toastCoalesceEndsUtc = now + s_toastCoalesceWindow;
                _toastArrivalCount = 1;
                _toastNotice = notice;
            }
            else
            {
                _toastArrivalCount++;

                if (now >= _toastCoalesceEndsUtc ||
                    _toastNotice is null ||
                    GetSeverityRank(notice.Severity) > GetSeverityRank(_toastNotice.Severity) ||
                    (notice.Severity == _toastNotice.Severity && notice.CreatedUtc >= _toastNotice.CreatedUtc))
                {
                    _toastNotice = notice;
                }
            }

            if (_toastNotice is { } selectedNotice)
            {
                _toast = new NotificationToast(
                    selectedNotice.Severity,
                    selectedNotice.Title,
                    selectedNotice.Message,
                    _toastArrivalCount);
            }

            _toastDeadlineUtc = Min(now + s_toastDuration, _toastBurstStartedUtc + s_toastBurstDuration);
            ScheduleToastTimerLocked(now);
            changed = true;
        }

        return changed;
    }

    private sealed record HostState(bool IsAvailable, bool IsModal);

    private sealed record Notice(
        string Identity,
        NotificationSeverity Severity,
        string Title,
        string Message,
        DateTime CreatedUtc,
        string MaterialSignature,
        bool IsToastEligible);
}
