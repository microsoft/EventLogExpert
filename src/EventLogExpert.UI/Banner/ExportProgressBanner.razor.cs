// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Banner;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.Banner;

public sealed partial class ExportProgressBanner : ComponentBase
{
    [Parameter] public RenderFragment? CycleNav { get; set; }

    [Parameter] public ExportProgressEntry Export { get; set; } = null!;

    [Inject] private ITraceLogger TraceLogger { get; init; } = null!;

    private Task OnCancelExportClickedAsync(ExportProgressEntry entry) =>
        BannerActionGuard.RunSafelyAsync(
            () => { entry.Cancel(); return Task.CompletedTask; },
            TraceLogger,
            nameof(ExportProgressBanner),
            nameof(OnCancelExportClickedAsync));
}
