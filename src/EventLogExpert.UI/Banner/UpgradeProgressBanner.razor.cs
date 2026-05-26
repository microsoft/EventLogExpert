// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Banner;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.Banner;

public sealed partial class UpgradeProgressBanner : ComponentBase
{
    [Parameter] public RenderFragment? CycleNav { get; set; }

    [Parameter] public BannerProgressEntry Progress { get; set; } = null!;

    [Inject] private ITraceLogger TraceLogger { get; init; } = null!;

    private Task OnCancelUpgradeClickedAsync(BannerProgressEntry entry) =>
        BannerActionGuard.RunSafelyAsync(
            () => { entry.Cancel(); return Task.CompletedTask; },
            TraceLogger,
            nameof(UpgradeProgressBanner),
            nameof(OnCancelUpgradeClickedAsync));
}
