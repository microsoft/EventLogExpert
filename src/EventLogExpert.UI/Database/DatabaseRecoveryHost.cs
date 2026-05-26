// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Common.Threading;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.Modal;
using EventLogExpert.UI.Modal;

namespace EventLogExpert.UI.Database;

/// <summary>
///     Coordinates the recovery banner and modal lifecycle in response to
///     <see cref="IDatabaseService.EntriesChanged" />. Registered as a singleton and force-instantiated in MauiProgram so
///     it begins observing state at startup. Crashes in event handlers route through
///     <see cref="IBannerService.ReportCritical(Exception)" /> to preserve the original Main.razor placement intent
///     (UnhandledExceptionHandler coverage equivalent).
/// </summary>
public sealed class DatabaseRecoveryHost : IDisposable
{
    private readonly IBannerService _bannerService;
    private readonly IDatabaseService _databaseService;
    private readonly IMainThreadService _mainThreadService;
    private readonly IModalCoordinator _modalCoordinator;
    private readonly ITraceLogger _traceLogger;
    private bool _disposed;
    private HashSet<string> _promptedFor = new(StringComparer.OrdinalIgnoreCase);
    private BannerId? _recoveryBannerId;

    public DatabaseRecoveryHost(
        IBannerService bannerService,
        IDatabaseService databaseService,
        IModalCoordinator modalCoordinator,
        ITraceLogger traceLogger,
        IMainThreadService mainThreadService)
    {
        ArgumentNullException.ThrowIfNull(bannerService);
        ArgumentNullException.ThrowIfNull(databaseService);
        ArgumentNullException.ThrowIfNull(modalCoordinator);
        ArgumentNullException.ThrowIfNull(traceLogger);
        ArgumentNullException.ThrowIfNull(mainThreadService);

        _bannerService = bannerService;
        _databaseService = databaseService;
        _modalCoordinator = modalCoordinator;
        _traceLogger = traceLogger;
        _mainThreadService = mainThreadService;

        _databaseService.EntriesChanged += OnEntriesChanged;
        _bannerService.StateChanged += OnBannerStateChanged;

        DispatchSafely(EvaluateState);
    }

    public void Dispose()
    {
        if (_disposed) { return; }

        _disposed = true;
        _databaseService.EntriesChanged -= OnEntriesChanged;
        _bannerService.StateChanged -= OnBannerStateChanged;

        if (_recoveryBannerId is not { } id) { return; }

        _bannerService.DismissError(id);
        _recoveryBannerId = null;
    }

    private void DismissCurrentBannerIfAny()
    {
        if (_recoveryBannerId is not { } activeId) { return; }

        _bannerService.DismissError(activeId);
        _recoveryBannerId = null;
    }

    private void DispatchSafely(Action action) => _ = DispatchSafelyAsync(action);

    private async Task DispatchSafelyAsync(Action action)
    {
        try
        {
            await _mainThreadService.InvokeOnMainThread(() => SafeInvoke(action));
        }
        catch (Exception dispatchEx)
        {
            _bannerService.ReportCritical(dispatchEx);
        }
    }

    private async Task DispatchSafelyAsync(Func<Task> asyncAction)
    {
        try
        {
            await _mainThreadService.InvokeOnMainThreadAsync(() => SafeInvokeAsync(asyncAction));
        }
        catch (Exception dispatchEx)
        {
            _bannerService.ReportCritical(dispatchEx);
        }
    }

    private void SafeInvoke(Action action)
    {
        try
        {
            action();
        }
        catch (Exception handlerEx)
        {
            _bannerService.ReportCritical(handlerEx);
        }
    }

    private async Task SafeInvokeAsync(Func<Task> asyncAction)
    {
        try
        {
            await asyncAction();
        }
        catch (Exception handlerEx)
        {
            _bannerService.ReportCritical(handlerEx);
        }
    }

    private void EvaluateState()
    {
        if (_disposed) { return; }

        HashSet<string> currentBackupSet = _databaseService.Entries
            .Where(entry => entry.BackupExists)
            .Select(entry => entry.FileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (currentBackupSet.Count == 0)
        {
            DismissCurrentBannerIfAny();
            _promptedFor.Clear();

            return;
        }

        if (currentBackupSet.SetEquals(_promptedFor)) { return; }

        DismissCurrentBannerIfAny();

        _recoveryBannerId = ReportRecoveryBanner(currentBackupSet.Count);
        _promptedFor = currentBackupSet;
    }

    private void HandleBannerStateChanged()
    {
        if (_disposed) { return; }

        if (_recoveryBannerId is not { } id) { return; }

        if (_bannerService.ErrorBanners.Any(banner => banner.Id == id)) { return; }

        _recoveryBannerId = null;
    }

    private void OnBannerStateChanged() => DispatchSafely(HandleBannerStateChanged);

    private void OnEntriesChanged(object? sender, EventArgs args) => DispatchSafely(EvaluateState);

    private Task OpenRecoveryDialogAsync() => DispatchSafelyAsync(async () =>
    {
        if (_disposed) { return; }

        if (!_databaseService.Entries.Any(entry => entry.BackupExists)) { return; }

        ModalOpenResult<bool> result = await _modalCoordinator.OpenDatabaseRecoveryAsync();

        if (!result.WasOpened)
        {
            _traceLogger.Trace(
                $"{nameof(DatabaseRecoveryDialog)} open preempted by an active modal.");
        }
    });

    private BannerId ReportRecoveryBanner(int count)
    {
        string message = count == 1
            ? "1 database needs recovery from interrupted upgrade."
            : $"{count} databases need recovery from interrupted upgrade.";

        return _bannerService.ReportError("Database upgrade recovery", message, "Resolve", OpenRecoveryDialogAsync);
    }
}
