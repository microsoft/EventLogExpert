// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.UI.Common;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.Banner;

public sealed partial class InfoBanner : ComponentBase
{
    private readonly string _titleId = ComponentId.NewUnique("notification-card-title").Value;

    [Parameter] public RenderFragment? CycleNav { get; set; }

    [Parameter] public BannerInfoEntry Entry { get; set; } = null!;

    [Parameter] public bool StaticPresentation { get; set; }

    [Inject] private IInfoBannerService InfoBannerService { get; init; } = null!;

    [Inject] private IStringLocalizer<SharedResource> Localizer { get; init; } = null!;

    private string SeverityClass => Entry.Severity == BannerSeverity.Warning ? "banner-warning" : "banner-info";

    private string SeverityIconClass =>
        Entry.Severity == BannerSeverity.Warning ?
            "bi bi-exclamation-triangle" :
            "bi bi-info-circle";

    private string SeverityText =>
        Entry.Severity == BannerSeverity.Warning ?
            Localizer["Notifications_Severity_Warning"].Value :
            Localizer["Notifications_Severity_Info"].Value;

    private void OnDismissInfo() => InfoBannerService.DismissInfoBanner(Entry.Id);
}
