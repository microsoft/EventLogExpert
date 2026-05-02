// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Components;

/// <summary>
///     Inline banner rendered inside <c>SettingsModal</c> that mirrors the top-level upgrade-progress card but
///     observes <see cref="IBannerService.SettingsProgress" /> instead of the background slot. Renders nothing
///     when no settings-scope upgrade batch is in flight.
/// </summary>
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
            TraceLogger.Error(
                $"{nameof(SettingsUpgradeProgressBanner)}.{nameof(OnCancelClickedAsync)}: cancel threw: {ex}");
        }

        await Task.CompletedTask;
    }

    private void OnStateChanged()
    {
        if (_disposed) { return; }

        _ = InvokeAsync(StateHasChanged);
    }
}
