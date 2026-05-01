// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Interfaces;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace EventLogExpert.Shared;

public partial class UnhandledExceptionHandler : ErrorBoundary, IDisposable
{
    private IDisposable? _recoveryRegistration;

    [Inject] private IBannerService BannerService { get; set; } = null!;

    [Inject] private ITraceLogger TraceLogger { get; set; } = null!;

    public void Dispose()
    {
        _recoveryRegistration?.Dispose();
    }

    protected override Task OnErrorAsync(Exception exception)
    {
        TraceLogger.Critical($"Unhandled exception in UI:\r\n{exception}");
        BannerService.ReportError(exception);

        return base.OnErrorAsync(exception);
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _recoveryRegistration = BannerService.RegisterRecoveryCallback(RecoverFromBannerAsync);
    }

    private Task RecoverFromBannerAsync()
    {
        Recover();
        return Task.CompletedTask;
    }
}
