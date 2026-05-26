// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Banner;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace EventLogExpert.UI.ErrorHandling;

public partial class UnhandledExceptionHandler : ErrorBoundary, IDisposable
{
    private IDisposable? _recoveryRegistration;

    [Inject] private ICriticalErrorService CriticalErrorService { get; set; } = null!;

    [Inject] private ITraceLogger TraceLogger { get; set; } = null!;

    public void Dispose()
    {
        _recoveryRegistration?.Dispose();
    }

    protected override Task OnErrorAsync(Exception exception)
    {
        TraceLogger.Critical($"Unhandled exception in UI:\r\n{exception}");
        CriticalErrorService.ReportCritical(exception);

        return base.OnErrorAsync(exception);
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();

        _recoveryRegistration = CriticalErrorService.RegisterRecoveryCallback(RecoverFromBannerAsync);
    }

    private Task RecoverFromBannerAsync()
    {
        Recover();

        return Task.CompletedTask;
    }
}
