// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Shared;

public partial class UnhandledExceptionHandler : ErrorBoundary
{
    [Inject] private ITraceLogger TraceLogger { get; set; } = null!;

    protected override Task OnErrorAsync(Exception exception)
    {
        TraceLogger.Trace($"Unhandled exception in UI:\r\n{exception}", LogLevel.Critical);

        return base.OnErrorAsync(exception);
    }
}
