// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Banner;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.Banner;

public sealed partial class BannerHost : ComponentBase, IDisposable
{
    [Parameter] public BannerHostLocation Location { get; set; } = BannerHostLocation.MainLayout;

    [Inject] private IAttentionBannerService AttentionBannerService { get; init; } = null!;

    [Inject] private ICriticalErrorService CriticalErrorService { get; init; } = null!;

    [Inject] private IBannerCycleStateService CycleState { get; init; } = null!;

    [Inject] private IErrorBannerService ErrorBannerService { get; init; } = null!;

    [Inject] private IInfoBannerService InfoBannerService { get; init; } = null!;

    [Inject] private IProgressBannerService ProgressBannerService { get; init; } = null!;

    private bool RendersContent => Location switch
    {
        BannerHostLocation.MainLayout => !CycleState.ModalContentDisplayed,
        BannerHostLocation.InsideModal => CycleState.ModalContentDisplayed,
        _ => false
    };

    public void Dispose() => CycleState.StateChanged -= OnCycleStateChanged;

    protected override void OnInitialized()
    {
        CycleState.StateChanged += OnCycleStateChanged;

        base.OnInitialized();
    }

    private void HandleFallbackErrorPosted(BannerCycleItem newCycleItem) =>
        CycleState.RegisterFallbackError(newCycleItem);

    private void OnCycleStateChanged() => _ = InvokeAsync(StateHasChanged);
}
