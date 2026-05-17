// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Logging;
using EventLogExpert.Runtime.Banner;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Components.Database;

public sealed partial class SettingsUpgradeProgressBanner : ComponentBase, IDisposable
{
    private bool _disposed;

    [Inject] private IBannerService BannerService { get; init; } = null!;

    [Inject] private ITraceLogger TraceLogger { get; init; } = null!;

    public void Dispose()
    {
        if (_disposed) { return; }

        _disposed = true;

        BannerService.StateChanged -= OnStateChanged;
    }

    protected override void OnInitialized()
    {
        BannerService.StateChanged += OnStateChanged;

        base.OnInitialized();
    }

    private async Task OnCancelClickedAsync(BannerProgressEntry entry)
    {
        try
        {
            entry.Cancel();
        }
        catch (Exception ex)
        {
            TraceLogger.Error($"{nameof(SettingsUpgradeProgressBanner)}.{nameof(OnCancelClickedAsync)}: cancel threw: {ex}");
        }

        await Task.CompletedTask;
    }

    private void OnStateChanged()
    {
        if (_disposed) { return; }

        _ = InvokeAsync(StateHasChanged);
    }
}
