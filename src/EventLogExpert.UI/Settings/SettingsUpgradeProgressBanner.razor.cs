// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Banner;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.Settings;

public sealed partial class SettingsUpgradeProgressBanner : ComponentBase, IDisposable
{
    private bool _disposed;

    [Inject] private IProgressBannerService ProgressBannerService { get; init; } = null!;

    [Inject] private ITraceLogger TraceLogger { get; init; } = null!;

    public void Dispose()
    {
        if (_disposed) { return; }

        _disposed = true;

        ProgressBannerService.StateChanged -= OnStateChanged;
    }

    protected override void OnInitialized()
    {
        ProgressBannerService.StateChanged += OnStateChanged;

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
