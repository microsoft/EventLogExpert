// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.UI.Common;
using EventLogExpert.UI.Common.Interop;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace EventLogExpert.UI.Banner;

public sealed partial class NotificationCenter : ComponentBase, IAsyncDisposable
{
    private readonly string _clearAllDescriptionId = ComponentId.NewUnique("notification-clear-all-description").Value;
    private readonly object _hostIdentity = new();
    private readonly string _panelId = ComponentId.NewUnique("notification-panel").Value;
    private readonly string _triggerId = ComponentId.NewUnique("notification-trigger").Value;

    private bool _clearInProgress;
    private bool _disposed;
    private DotNetObjectReference<NotificationCenter>? _dotNetReference;
    private bool? _lastRenderedTriggerShown;
    private bool? _lastReportedTriggerShown;
    private IJSObjectReference? _module;
    private ElementReference _panel;
    private long _retireGeneration;
    private bool _retirePending;
    private ElementReference _toastElement;
    private ElementReference _trigger;
    private bool _triggerShown;

    [Parameter] public bool Disabled { get; set; }

    [Parameter] public BannerHostLocation Location { get; set; } = BannerHostLocation.MainLayout;

    [Parameter] public bool ShowNotifications { get; set; } = true;

    [Parameter] public EventCallback<bool> TriggerVisibilityChanged { get; set; }

    [Inject] private IAlertDialogService AlertDialogService { get; init; } = null!;

    [Inject] private NotificationAnnouncementState AnnouncementState { get; init; } = null!;

    [Inject] private IAttentionBannerService AttentionBannerService { get; init; } = null!;

    [Inject] private INotificationBulkDismissService BulkDismissService { get; init; } = null!;

    [Inject] private ICriticalErrorService CriticalErrorService { get; init; } = null!;

    [Inject] private IBannerCycleStateService CycleState { get; init; } = null!;

    [Inject] private IErrorBannerService ErrorBannerService { get; init; } = null!;

    [Inject] private IExportProgressBannerService ExportProgressBannerService { get; init; } = null!;

    [Inject] private IInfoBannerService InfoBannerService { get; init; } = null!;

    private bool IsActiveHost => ShowNotifications && IsAvailable && AnnouncementState.IsActiveHost(_hostIdentity);

    private bool IsAvailable =>
        ShowNotifications &&
        !Disabled &&
        CriticalErrorService.CurrentCritical is null &&
        (Location == BannerHostLocation.MainLayout ?
            !CycleState.ModalContentDisplayed :
            CycleState.ModalContentDisplayed);

    private bool IsUnavailable => !IsAvailable;

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    [Inject] private IStringLocalizer<SharedResource> Localizer { get; init; } = null!;

    [Inject] private IProgressBannerService ProgressBannerService { get; init; } = null!;

    [Inject] private ITraceLogger TraceLogger { get; init; } = null!;

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        CycleState.StateChanged -= OnStateChanged;
        AnnouncementState.StateChanged -= OnStateChanged;
        AnnouncementState.UnregisterHost(_hostIdentity);
        _retireGeneration++;
        _retirePending = false;
        _triggerShown = false;

        await JsModuleInterop.DisposeModuleSafelyAsync(
            _module,
            module => module.InvokeVoidAsync("detach", _panel));

        _module = null;
        _dotNetReference?.Dispose();
        _dotNetReference = null;
    }

    [JSInvokable]
    public Task OnPopoverToggle(bool isOpen)
    {
        if (_disposed) { return Task.CompletedTask; }

        return InvokeAsync(() =>
        {
            if (_disposed) { return; }

            AnnouncementState.SetPanelOpen(_hostIdentity, isOpen);

            StateHasChanged();
        });
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        try
        {
            bool triggerVisible = ShowNotifications && _triggerShown;

            if (!_disposed && _lastReportedTriggerShown != triggerVisible)
            {
                _lastReportedTriggerShown = triggerVisible;

                await TriggerVisibilityChanged.InvokeAsync(triggerVisible);
            }

            if (_disposed) { return; }

            if (firstRender && ShowNotifications)
            {
                IJSObjectReference module = await JSRuntime.InvokeAsync<IJSObjectReference>(
                    "import",
                    "./_content/EventLogExpert.UI/Banner/NotificationCenter.razor.js");

                if (_disposed)
                {
                    await JsModuleInterop.DisposeModuleSafelyAsync(module);

                    return;
                }

                _module = module;
                _dotNetReference = DotNetObjectReference.Create(this);

                await module.InvokeVoidAsync("attach", _trigger, _panel, _dotNetReference, _toastElement);
            }

            if (_module is { } activeModule && !_disposed)
            {
                await activeModule.InvokeVoidAsync("setUnavailable", _panel, IsUnavailable);

                if (_disposed) { return; }

                if (_lastRenderedTriggerShown != _triggerShown)
                {
                    _lastRenderedTriggerShown = _triggerShown;
                    await activeModule.InvokeVoidAsync("slotChanged", _panel);

                    if (_disposed) { return; }
                }

                await activeModule.InvokeVoidAsync("positionToast",
                    _toastElement,
                    AnnouncementState.GetToast(_hostIdentity) is not null);
            }
        }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
        catch (ObjectDisposedException) when (_disposed) { }
        catch (JSException) when (_disposed) { }
    }

    protected override void OnInitialized()
    {
        CycleState.StateChanged += OnStateChanged;
        AnnouncementState.StateChanged += OnStateChanged;
        _triggerShown = GetActiveCount() > 0;
    }

    protected override void OnParametersSet() =>
        AnnouncementState.SetHostAvailability(_hostIdentity, IsAvailable, Location == BannerHostLocation.InsideModal);

    private async Task ClearAllAsync()
    {
        if (_clearInProgress || !IsAvailable) { return; }

        _clearInProgress = true;
        AnnouncementState.BeginClear();

        try
        {
            bool includeAttention = Location != BannerHostLocation.InsideModal ||
                CycleState.Items.Any(item => item.View == BannerView.Attention);

            NotificationDismissSnapshot snapshot = BulkDismissService.CaptureDismissSnapshot(includeAttention);
            if (snapshot.Count < 2) { return; }

            await PrepareModalActionAsync();

            if (_disposed) { return; }

            string message = PluralText.Format(Localizer, "Notifications_ClearConfirmation", ("count", snapshot.Count));

            if (snapshot.AttentionVersion.HasValue)
            {
                message += Localizer["Notifications_ClearAttentionWarning"].Value;
            }

            bool includesRecoveryReminder =
                snapshot.Errors.Any(entry => entry.Content is Preformatted { IsRecoveryReminder: true });

            if (includesRecoveryReminder)
            {
                message += Localizer["Notifications_ClearRecoveryWarning"].Value;
            }

            if (snapshot.AttentionVersion.HasValue || includesRecoveryReminder)
            {
                message += Localizer["Notifications_ClearDatabaseToolsHint"].Value;
            }

            bool accepted = await AlertDialogService.ShowAlert(
                Localizer["Notifications_ClearTitle"],
                message,
                Localizer["Notifications_ClearAll"],
                Localizer["Modal_Cancel"],
                AlertPresentation.Auto,
                defaultToCancel: true);

            if (_disposed) { return; }

            if (accepted)
            {
                int removedCount = BulkDismissService.DismissSnapshot(snapshot);
                AnnouncementState.AnnounceCleared(removedCount);
            }

            if (_module is not null)
            {
                await _module.InvokeVoidAsync("completeClearFocus",
                    _panel,
                    accepted && GetActiveCount() == 0 ? "empty" : "remaining");
            }
        }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
        catch (JSException) when (_disposed) { }
        catch (ObjectDisposedException) when (_disposed) { }
        finally
        {
            AnnouncementState.EndClear();
            _clearInProgress = false;
            if (!_disposed) { UpdateTriggerVisibility(GetActiveCount()); }
        }
    }

    private int GetActiveCount()
    {
        int attentionCount =
            AttentionBannerService.AttentionEntries.Count > 0 && !AttentionBannerService.AttentionDismissed ? 1 : 0;

        return ErrorBannerService.ErrorBanners.Count +
            InfoBannerService.InfoBanners.Count +
            attentionCount +
            (ProgressBannerService.BackgroundProgress is not null ? 1 : 0) +
            (ExportProgressBannerService.CurrentExport is not null ? 1 : 0);
    }

    private void OnStateChanged()
    {
        if (_disposed) { return; }

        _ = RefreshAfterSourceChangeAsync();
    }

    private async Task PrepareModalActionAsync()
    {
        try
        {
            if (_module is not null)
            {
                await _module.InvokeVoidAsync("prepareModalAction", _panel);
            }
        }
        catch (JSException) when (_disposed) { }
        catch (ObjectDisposedException) when (_disposed) { }
    }

    private async Task RefreshAfterSourceChangeAsync()
    {
        try
        {
            await InvokeAsync(() =>
            {
                if (_disposed) { return; }

                AnnouncementState.SetHostAvailability(_hostIdentity,
                    IsAvailable,
                    Location == BannerHostLocation.InsideModal);

                UpdateTriggerVisibility(GetActiveCount());
                StateHasChanged();
            }).ConfigureAwait(false);
        }
        catch (ObjectDisposedException) when (_disposed) { }
        catch (Exception exception)
        {
            TraceLogger.Error($"{nameof(NotificationCenter)}: failed to refresh after a notification source change: {exception}");
        }
    }

    private async Task RetireTriggerAsync(long generation)
    {
        try
        {
            if (_module is not null)
            {
                await _module.InvokeVoidAsync("retireFocusBeforeHide", _panel);
            }
        }
        catch (JSDisconnectedException) { }
        catch (JSException exception)
        {
            if (!_disposed)
            {
                TraceLogger.Warning($"{nameof(NotificationCenter)}: retireFocusBeforeHide failed: {exception}");
            }
        }
        catch (TaskCanceledException) { }
        catch (ObjectDisposedException exception)
        {
            if (!_disposed)
            {
                TraceLogger.Warning($"{nameof(NotificationCenter)}: retireFocusBeforeHide failed: {exception}");
            }
        }
        finally
        {
            if (generation == _retireGeneration)
            {
                _retirePending = false;
                _triggerShown = false;

                if (!_disposed)
                {
                    await InvokeAsync(StateHasChanged);
                }
            }
        }
    }

    private void UpdateTriggerVisibility(int activeCount)
    {
        if (activeCount > 0)
        {
            _retireGeneration++;
            _retirePending = false;
            _triggerShown = true;

            return;
        }

        if (_triggerShown && !_retirePending && !_clearInProgress)
        {
            _retirePending = true;
            long generation = ++_retireGeneration;
            _ = RetireTriggerAsync(generation);
        }
    }
}
