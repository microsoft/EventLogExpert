// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.Banner;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.Banner;

public sealed partial class InfoBanner : ComponentBase
{
    [Parameter] public RenderFragment? CycleNav { get; set; }

    [Parameter] public BannerInfoEntry Entry { get; set; } = null!;

    [Inject] private IInfoBannerService InfoBannerService { get; init; } = null!;

    [Inject] private IStringLocalizer<SharedResource> Localizer { get; init; } = null!;

    private string SeverityClass => Entry.Severity == BannerSeverity.Warning ? "banner-warning" : "banner-info";

    private void OnDismissInfo() => InfoBannerService.DismissInfoBanner(Entry.Id);
}
