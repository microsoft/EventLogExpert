// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.UI.Common;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.Banner;

public sealed partial class UpgradeProgressBanner : ComponentBase
{
    private readonly string _titleId = ComponentId.NewUnique("notification-card-title").Value;

    [Parameter] public RenderFragment? CycleNav { get; set; }

    [Parameter] public BannerProgressEntry Progress { get; set; } = null!;

    [Parameter] public bool StaticPresentation { get; set; }

    [Inject] private IStringLocalizer<SharedResource> Localizer { get; init; } = null!;

    [Inject] private ITraceLogger TraceLogger { get; init; } = null!;

    private Task OnCancelUpgradeClickedAsync(BannerProgressEntry entry) =>
        BannerActionGuard.RunSafelyAsync(
            () => { entry.Cancel(); return Task.CompletedTask; },
            TraceLogger,
            nameof(UpgradeProgressBanner),
            nameof(OnCancelUpgradeClickedAsync));
}
