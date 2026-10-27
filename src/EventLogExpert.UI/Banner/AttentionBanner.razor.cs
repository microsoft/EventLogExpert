// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.UI.Common;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace EventLogExpert.UI.Banner;

public sealed partial class AttentionBanner : ComponentBase
{
    private readonly string _actionHintId = ComponentId.NewUnique("notification-action-hint").Value;
    private readonly string _titleId = ComponentId.NewUnique("notification-card-title").Value;

    [Parameter] public bool ActionsDisabled { get; set; }

    [Parameter] public int AttentionCount { get; set; }

    [Parameter] public EventCallback BeforeAction { get; set; }

    [Parameter] public RenderFragment? CycleNav { get; set; }

    [Parameter] public EventCallback<BannerCycleItem> OnFallbackErrorPosted { get; set; }

    [Parameter] public bool StaticPresentation { get; set; }

    [Inject] private IAttentionBannerService AttentionBannerService { get; init; } = null!;

    [Inject] private IErrorBannerService ErrorBannerService { get; init; } = null!;

    [Inject] private IStringLocalizer<SharedResource> Localizer { get; init; } = null!;

    [Inject] private IMenuActionService MenuActionService { get; init; } = null!;

    [Inject] private ITraceLogger TraceLogger { get; init; } = null!;

    private void OnDismissAttention() => AttentionBannerService.DismissAttention();

    private async Task OnOpenDatabasesClickedAsync()
    {
        await BeforeAction.InvokeAsync();
        AttentionBannerService.DismissAttention();

        bool success;

        try
        {
            success = await MenuActionService.OpenDatabaseToolsAsync();
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
            TraceLogger.Error(
                $"{nameof(AttentionBanner)}.{nameof(OnOpenDatabasesClickedAsync)}: open databases threw: {ex}");

            BannerId errorId = ErrorBannerService.ReportError(new Preformatted(
                Localizer["Banner_Attention_ErrorTitle"],
                Localizer["Banner_Attention_OpenFailedDetail", ex.Message]));
            await OnFallbackErrorPosted.InvokeAsync(new BannerCycleItem(BannerView.Error, 0, errorId));

            return;
        }

        if (!success)
        {
            TraceLogger.Error(
                $"{nameof(AttentionBanner)}.{nameof(OnOpenDatabasesClickedAsync)}: open databases returned false");

            BannerId errorId = ErrorBannerService.ReportError(new Preformatted(
                Localizer["Banner_Attention_ErrorTitle"],
                Localizer["Banner_Attention_OpenFailed"]));
            await OnFallbackErrorPosted.InvokeAsync(new BannerCycleItem(BannerView.Error, 0, errorId));
        }
    }
}
