// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.UI.Common;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.Banner;

public sealed partial class ErrorBanner : ComponentBase
{
    private readonly string _actionHintId = ComponentId.NewUnique("notification-action-hint").Value;
    private readonly string _titleId = ComponentId.NewUnique("notification-card-title").Value;

    [Parameter] public bool ActionsDisabled { get; set; }

    [Parameter] public EventCallback BeforeAction { get; set; }

    [Parameter] public RenderFragment? CycleNav { get; set; }

    [Parameter] public ErrorBannerEntry Entry { get; set; } = null!;

    [Parameter] public bool StaticPresentation { get; set; }

    [Inject] private IErrorBannerService ErrorBannerService { get; init; } = null!;

    [Inject] private IStringLocalizer<SharedResource> Localizer { get; init; } = null!;

    [Inject] private ITraceLogger TraceLogger { get; init; } = null!;

    private void OnDismissError() => ErrorBannerService.DismissError(Entry.Id);

    private async Task OnErrorActionClickedAsync(Func<Task> action)
    {
        await BeforeAction.InvokeAsync();
        await BannerActionGuard.RunSafelyAsync(
            action,
            TraceLogger,
            nameof(ErrorBanner),
            nameof(OnErrorActionClickedAsync));
    }
}
