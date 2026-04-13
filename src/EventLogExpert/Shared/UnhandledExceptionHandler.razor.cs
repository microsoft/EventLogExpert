// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace EventLogExpert.Shared;

public partial class UnhandledExceptionHandler : ErrorBoundary
{
    [Inject] private ITraceLogger TraceLogger { get; set; } = null!;

    protected override Task OnErrorAsync(Exception exception)
    {
        TraceLogger.Critical($"Unhandled exception in UI:\r\n{exception}");

        return base.OnErrorAsync(exception);
    }
}
