// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Menu;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace EventLogExpert.UI.Banner;

public sealed partial class AttentionBanner : ComponentBase
{
    [Parameter] public int AttentionCount { get; set; }

    [Parameter] public RenderFragment? CycleNav { get; set; }

    [Parameter] public EventCallback<BannerCycleItem> OnFallbackErrorPosted { get; set; }

    [Inject] private IAttentionBannerService AttentionBannerService { get; init; } = null!;

    private string DatabasesLabel => AttentionCount == 1 ? "database" : "databases";

    [Inject] private IErrorBannerService ErrorBannerService { get; init; } = null!;

    [Inject] private IMenuActionService MenuActionService { get; init; } = null!;

    private string NeedsLabel => AttentionCount == 1 ? "needs" : "need";

    [Inject] private ITraceLogger TraceLogger { get; init; } = null!;

    private void OnDismissAttention() => AttentionBannerService.DismissAttention();

    private async Task OnOpenDatabasesClickedAsync()
    {
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

            BannerId errorId = ErrorBannerService.ReportError(
                "Databases",
                $"Failed to open databases: {ex.Message}");
            await OnFallbackErrorPosted.InvokeAsync(new BannerCycleItem(BannerView.Error, 0, errorId));

            return;
        }

        if (!success)
        {
            TraceLogger.Error(
                $"{nameof(AttentionBanner)}.{nameof(OnOpenDatabasesClickedAsync)}: open databases returned false");

            BannerId errorId = ErrorBannerService.ReportError(
                "Databases",
                "Failed to open databases; try again from the menu.");
            await OnFallbackErrorPosted.InvokeAsync(new BannerCycleItem(BannerView.Error, 0, errorId));
        }
    }
}
