// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Banner;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.Banner;

public sealed partial class ErrorBanner : ComponentBase
{
    [Parameter] public RenderFragment? CycleNav { get; set; }

    [Parameter] public ErrorBannerEntry Entry { get; set; } = null!;

    [Inject] private IErrorBannerService ErrorBannerService { get; init; } = null!;

    [Inject] private ITraceLogger TraceLogger { get; init; } = null!;

    private void OnDismissError() => ErrorBannerService.DismissError(Entry.Id);

    private Task OnErrorActionClickedAsync(Func<Task> action) =>
        BannerActionGuard.RunSafelyAsync(
            action,
            TraceLogger,
            nameof(ErrorBanner),
            nameof(OnErrorActionClickedAsync));
}
